using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace BuildingBlocks.Mcp.Invocation;

/// <summary>
/// Default <see cref="IMcpInvoker"/>: filters, rate limit, timeout, idempotency, map results. Does not retry writes.
/// </summary>
public sealed class McpInvoker : IMcpInvoker
{
	private readonly IReadOnlyList<McpToolDescriptor> _catalog;
	private readonly IServiceProvider _services;
	private readonly IEnumerable<IMcpToolFilter> _filters;
	private readonly IMcpRateLimiter _rateLimiter;
	private readonly IMcpResultMapper _mapper;
	private readonly IMcpMessageDispatcher? _dispatcher;
	private readonly IMcpIdempotencyStore? _idempotency;
	private readonly IMcpResiliencePolicy? _resilience;
	private readonly McpTelemetry? _telemetry;
	private readonly bool _includeExceptionDetails;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _idempotencyGates = new(StringComparer.Ordinal);

	/// <summary>
	/// Creates the invoker.
	/// </summary>
	public McpInvoker(
		IReadOnlyList<McpToolDescriptor> catalog,
		IServiceProvider services,
		IEnumerable<IMcpToolFilter> filters,
		IMcpRateLimiter rateLimiter,
		IMcpResultMapper mapper,
		IMcpMessageDispatcher? dispatcher,
		IMcpIdempotencyStore? idempotency,
		IMcpResiliencePolicy? resilience,
		McpTelemetry? telemetry,
		bool includeExceptionDetails)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_filters = filters ?? [];
		_rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
		_mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
		_dispatcher = dispatcher;
		_idempotency = idempotency;
		_resilience = resilience;
		_telemetry = telemetry;
		_includeExceptionDetails = includeExceptionDetails;
	}

	/// <inheritdoc />
	public IReadOnlyList<McpToolDescriptor> Catalog => _catalog;

	/// <inheritdoc />
	public async Task<IReadOnlyList<McpToolDescriptor>> ListVisibleAsync(
		McpInvokeContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		var visible = new List<McpToolDescriptor>();
		foreach (var tool in _catalog)
		{
			if (await IsVisibleAsync(tool, context, cancellationToken).ConfigureAwait(false))
				visible.Add(tool);
		}

		return visible;
	}

	/// <inheritdoc />
	public async Task<McpResult<object?>> InvokeAsync(
		string toolName,
		JsonElement arguments,
		McpInvokeContext context,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		ArgumentNullException.ThrowIfNull(context);

		var tool = _catalog.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
		if (tool is null)
			return McpResult.Fail<object?>(McpErrorCode.NotFound, $"Unknown tool '{toolName}'.");

		if (!await IsVisibleAsync(tool, context, cancellationToken).ConfigureAwait(false))
			return McpResult.Fail<object?>(McpErrorCode.Forbidden, $"Tool '{toolName}' is not available.");

		var limit = await _rateLimiter.TryAcquireAsync(toolName, context, cancellationToken).ConfigureAwait(false);
		if (!limit.Allowed)
		{
			return McpResult.Fail<object?>(new McpError(
				McpErrorCode.RateLimited,
				$"Tool '{toolName}' is rate limited.",
				retryAfterSeconds: limit.RetryAfterSeconds));
		}

		var args = NormalizeArgs(arguments);
		var ctx = EnrichContext(context, tool, args);

		if (tool.Kind == McpToolKind.Command && tool.Idempotent && string.IsNullOrWhiteSpace(ctx.IdempotencyKey))
			return McpResult.Fail<object?>(McpErrorCode.IdempotencyKeyRequired, $"Tool '{toolName}' requires '{McpDefaults.IdempotencyKeyArgument}'.");

		if (tool.RequireConfirmation && !ctx.Confirmed)
			return McpResult.Fail<object?>(McpErrorCode.ConfirmationRequired, $"Tool '{toolName}' requires '{McpDefaults.ConfirmedArgument}' to be true.");

		if (tool.Kind == McpToolKind.Command && tool.Idempotent && ctx.IdempotencyKey is not null && _idempotency is not null)
		{
			var cacheKey = CacheKey(tool.Name, ctx.IdempotencyKey);
			var gate = _idempotencyGates.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
			await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				var cached = await _idempotency.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
				if (cached is not null)
					return McpResult.Ok<object?>(ParseCachedPayload(cached));

				var computed = await InvokeCoreAsync(tool, args, ctx, cancellationToken).ConfigureAwait(false);
				if (computed.IsSuccess)
				{
					var json = JsonSerializer.Serialize(computed.Value, McpJson.Options);
					await _idempotency.SetAsync(cacheKey, json, cancellationToken).ConfigureAwait(false);
				}

				return computed;
			}
			finally
			{
				gate.Release();
			}
		}

		return await InvokeCoreAsync(tool, args, ctx, cancellationToken).ConfigureAwait(false);
	}

	private async Task<McpResult<object?>> InvokeCoreAsync(
		McpToolDescriptor tool,
		JsonElement args,
		McpInvokeContext ctx,
		CancellationToken cancellationToken)
	{
		object message;
		try
		{
			var messageJson = StripProtocolArgs(args);
			message = JsonSerializer.Deserialize(messageJson, tool.MessageType, McpJson.Options)
				?? throw new JsonException("Message deserialized to null.");
		}
		catch (JsonException ex)
		{
			return McpResult.Fail<object?>(McpErrorCode.Validation, $"Invalid arguments: {ex.Message}");
		}

		Activity? activity = null;
		if (_telemetry?.Enabled == true)
		{
			activity = _telemetry.Source.StartActivity("mcp.tool", ActivityKind.Internal);
			activity?.SetTag("mcp.tool.name", tool.Name);
			activity?.SetTag("mcp.tool.kind", tool.Kind.ToString());
			if (ctx.IdempotencyKey is not null)
				activity?.SetTag("mcp.idempotency_key", "present");
		}

		var accessor = _services.GetService<McpInvokeContextAccessor>();
		using (accessor?.Push(ctx))
		using (activity)
		{
			try
			{
				using var timeoutCts = tool.Timeout is { } t
					? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
					: null;
				if (timeoutCts is not null)
					timeoutCts.CancelAfter(tool.Timeout!.Value);

				var ct = timeoutCts?.Token ?? cancellationToken;

				async Task<McpResult<object?>> Core(CancellationToken token)
				{
					if (tool.Handler is not null)
						return await tool.Handler(_services, message, ctx, token).ConfigureAwait(false);

					if (_dispatcher is null)
						throw new McpInvocationException($"No dispatcher or MapTool handler for '{tool.Name}'.");

					var raw = await _dispatcher.SendAsync(message, token).ConfigureAwait(false);
					return _mapper.Map(raw);
				}

				McpResult<object?> result;
				try
				{
					if (_resilience is not null && tool.Kind == McpToolKind.Query)
						result = await _resilience.ExecuteAsync(Core, tool.Kind, ct).ConfigureAwait(false);
					else
						result = await Core(ct).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
				{
					activity?.SetStatus(ActivityStatusCode.Error, "timeout");
					return McpResult.Fail<object?>(McpErrorCode.Timeout, $"Tool '{tool.Name}' timed out.");
				}
				catch (OperationCanceledException)
				{
					activity?.SetStatus(ActivityStatusCode.Error, "canceled");
					return McpResult.Fail<object?>(McpErrorCode.Canceled, "The MCP tool call was canceled.");
				}

				activity?.SetTag("mcp.success", result.IsSuccess);
				if (!result.IsSuccess && result.Error is not null)
					activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code.ToString());

				return result;
			}
			catch (Exception ex) when (ex is not McpCatalogException and not McpInvocationException)
			{
				activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
				var messageText = _includeExceptionDetails
					? ex.Message
					: "The tool failed.";
				return McpResult.Fail<object?>(McpErrorCode.Internal, messageText);
			}
		}
	}

	private async ValueTask<bool> IsVisibleAsync(
		McpToolDescriptor tool,
		McpInvokeContext context,
		CancellationToken cancellationToken)
	{
		if (tool.Roles.Count > 0)
		{
			if (context.User is null || !tool.Roles.Any(r => context.User.IsInRole(r)))
				return false;
		}

		foreach (var filter in _filters)
		{
			if (!await filter.IsVisibleAsync(tool, context, cancellationToken).ConfigureAwait(false))
				return false;
		}

		return true;
	}

	private static McpInvokeContext EnrichContext(McpInvokeContext context, McpToolDescriptor tool, JsonElement args)
	{
		var key = ReadString(args, McpDefaults.IdempotencyKeyArgument) ?? context.IdempotencyKey;
		var dryRun = tool.AllowDryRun && (ReadBool(args, McpDefaults.DryRunArgument) || context.DryRun);
		var confirmed = tool.RequireConfirmation
			? ReadBool(args, McpDefaults.ConfirmedArgument) || context.Confirmed
			: context.Confirmed;
		return context with { IdempotencyKey = key, DryRun = dryRun, Confirmed = confirmed };
	}

	private static JsonElement NormalizeArgs(JsonElement arguments)
	{
		if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
			return JsonDocument.Parse("{}").RootElement.Clone();
		return arguments.Clone();
	}

	private static string StripProtocolArgs(JsonElement args)
	{
		if (args.ValueKind != JsonValueKind.Object)
			return args.GetRawText();

		using var doc = JsonDocument.Parse(args.GetRawText());
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			foreach (var p in doc.RootElement.EnumerateObject())
			{
				if (p.NameEquals(McpDefaults.IdempotencyKeyArgument)
					|| p.NameEquals(McpDefaults.DryRunArgument)
					|| p.NameEquals(McpDefaults.ConfirmedArgument))
					continue;
				p.WriteTo(writer);
			}

			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	private static string? ReadString(JsonElement args, string name)
	{
		if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
			return p.GetString();
		return null;
	}

	private static bool ReadBool(JsonElement args, string name)
	{
		if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p))
		{
			if (p.ValueKind == JsonValueKind.True) return true;
			if (p.ValueKind == JsonValueKind.False) return false;
			if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b)) return b;
		}

		return false;
	}

	internal static string CacheKey(string toolName, string idempotencyKey)
		=> toolName + "\u001f" + idempotencyKey;

	private static object? ParseCachedPayload(string json)
	{
		using var doc = JsonDocument.Parse(json);
		return doc.RootElement.Clone();
	}
}
