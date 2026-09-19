using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BuildingBlocks.Mcp.Protocol;

/// <summary>
/// Wires official MCP list/call/resource handlers to <see cref="IMcpInvoker"/>.
/// </summary>
internal static class McpProtocolRegistration
{
	public static void AddMcpServer(IServiceCollection services, bool stdio)
	{
		var mcp = services.AddMcpServer();
		if (stdio)
			mcp.WithStdioServerTransport();
		else
			mcp.WithHttpTransport();

		mcp.WithListToolsHandler(ListToolsAsync)
			.WithCallToolHandler(CallToolAsync)
			.WithListResourcesHandler(ListResourcesAsync)
			.WithReadResourceHandler(ReadResourceAsync);
	}

	private static async ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
	{
		var invoker = GetInvoker(request.Services);
		var ctx = CreateContext(request.Services);
		var tools = await invoker.ListVisibleAsync(ctx, cancellationToken).ConfigureAwait(false);
		return new ListToolsResult
		{
			Tools = [.. tools.Select(ToTool)]
		};
	}

	private const string ConfirmationInputRequestKey = "confirmation";
	private const string ConfirmationRequestState = "awaiting-confirmation";

	private static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
	{
		var invoker = GetInvoker(request.Services);
		var name = request.Params?.Name ?? string.Empty;
		JsonElement args = default;
		if (request.Params?.Arguments is { Count: > 0 } dict)
			args = JsonSerializer.SerializeToElement(dict, McpJson.Options);

		if (IsConfirmationDeclined(request.Params))
			return ToErrorCallResult(ConfirmationRequired(name));

		var ctx = CreateContext(request.Services, confirmed: IsConfirmationAccepted(request.Params));
		var result = await invoker.InvokeAsync(name, args, ctx, cancellationToken).ConfigureAwait(false);
		if (result.IsSuccess)
			return ToSuccessCallResult(result.Value);

		if (result.Error?.Code == McpErrorCode.ConfirmationRequired && request.Server.IsMrtrSupported)
			throw CreateConfirmationInputRequired(name);

		return ToErrorCallResult(result);
	}

	private static ValueTask<ListResourcesResult> ListResourcesAsync(RequestContext<ListResourcesRequestParams> request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(new ListResourcesResult
		{
			Resources =
			[
				new Resource
				{
					Uri = McpDefaults.CatalogResourceUri,
					Name = "Tool catalog",
					Description = "Markdown table of enabled MCP tools.",
					MimeType = "text/markdown"
				}
			]
		});
	}

	private static async ValueTask<ReadResourceResult> ReadResourceAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken)
	{
		var uri = request.Params?.Uri ?? string.Empty;
		if (!IsCatalogResourceUri(uri))
		{
			return new ReadResourceResult
			{
				Contents = [new TextResourceContents { Uri = uri, Text = "Unknown resource.", MimeType = "text/plain" }]
			};
		}

		var invoker = GetInvoker(request.Services);
		var ctx = CreateContext(request.Services);
		var tools = await invoker.ListVisibleAsync(ctx, cancellationToken).ConfigureAwait(false);
		var md = ToolCatalogMarkdown.Render(tools);
		return new ReadResourceResult
		{
			Contents = [new TextResourceContents { Uri = McpDefaults.CatalogResourceUri, Text = md, MimeType = "text/markdown" }]
		};
	}

	internal static CallToolResult ToSuccessCallResult(object? value)
	{
		var payload = JsonSerializer.SerializeToElement(value, McpJson.Options);
		var structured = payload.ValueKind == JsonValueKind.Object
			? payload
			: JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["value"] = value }, McpJson.Options);
		var text = payload.ValueKind == JsonValueKind.String
			? payload.GetString() ?? string.Empty
			: payload.GetRawText();
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = structured
		};
	}

	/// <summary>
	/// <see cref="Uri"/> turns <c>catalog://tools</c> into <c>catalog://tools/</c> (host + empty path).
	/// </summary>
	internal static bool IsCatalogResourceUri(string? uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
			return false;
		var normalized = uri.Trim().TrimEnd('/');
		return string.Equals(normalized, McpDefaults.CatalogResourceUri, StringComparison.OrdinalIgnoreCase);
	}

	private static IMcpInvoker GetInvoker(IServiceProvider? services)
		=> (services ?? throw new InvalidOperationException("MCP request has no IServiceProvider."))
			.GetRequiredService<IMcpInvoker>();

	private static McpInvokeContext CreateContext(IServiceProvider? services, bool confirmed = false)
	{
		if (services is null)
			return McpInvokeContext.None;
		var accessor = services.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
		var user = accessor?.HttpContext?.User;
		return new McpInvokeContext(user, null, DryRun: false, Confirmed: confirmed);
	}

	private static CallToolResult ToErrorCallResult(McpResult<object?> result)
	{
		var errorJson = JsonSerializer.Serialize(result.Error, McpJson.Options);
		return new CallToolResult
		{
			IsError = true,
			Content = [new TextContentBlock { Text = errorJson }]
		};
	}

	private static McpResult<object?> ConfirmationRequired(string toolName)
		=> McpResult.Fail<object?>(
			McpErrorCode.ConfirmationRequired,
			$"Tool '{toolName}' requires '{McpDefaults.ConfirmedArgument}' to be true.");

	private static InputRequiredException CreateConfirmationInputRequired(string toolName)
	{
		return new InputRequiredException(
			new Dictionary<string, InputRequest>
			{
				[ConfirmationInputRequestKey] = InputRequest.ForElicitation(new ElicitRequestParams
				{
					Message = $"Confirm execution of '{toolName}'. Accept to proceed, or decline to cancel.",
					RequestedSchema = new ElicitRequestParams.RequestSchema
					{
						Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
						{
							[McpDefaults.ConfirmedArgument] = new ElicitRequestParams.BooleanSchema
							{
								Description = "Must be true to execute this write."
							}
						},
						Required = [McpDefaults.ConfirmedArgument]
					}
				})
			},
			requestState: ConfirmationRequestState);
	}

	private static bool IsConfirmationAccepted(CallToolRequestParams? parameters)
	{
		if (!TryReadConfirmationElicit(parameters, out var elicit) || elicit is null)
			return false;
		if (!elicit.IsAccepted)
			return false;
		if (elicit.Content is { } content
			&& content.TryGetValue(McpDefaults.ConfirmedArgument, out var confirmed)
			&& IsJsonFalse(confirmed))
			return false;
		return true;
	}

	private static bool IsConfirmationDeclined(CallToolRequestParams? parameters)
	{
		if (!TryReadConfirmationElicit(parameters, out var elicit) || elicit is null)
			return false;
		return !IsConfirmationAccepted(parameters);
	}

	private static bool TryReadConfirmationElicit(CallToolRequestParams? parameters, out ElicitResult? elicit)
	{
		elicit = null;
		if (parameters?.InputResponses is not { Count: > 0 } responses)
			return false;
		if (!responses.TryGetValue(ConfirmationInputRequestKey, out var response))
			return false;
		elicit = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
		return elicit is not null;
	}

	private static bool IsJsonFalse(JsonElement value)
		=> value.ValueKind == JsonValueKind.False
			|| (value.ValueKind == JsonValueKind.String
				&& bool.TryParse(value.GetString(), out var parsed)
				&& !parsed);

	internal static Tool ToTool(McpToolDescriptor d)
	{
		var properties = new Dictionary<string, JsonElement>();
		var required = new List<string>();
		foreach (var p in d.Properties)
		{
			properties[p.Name] = JsonSerializer.SerializeToElement(ToPropertySchema(p));
			if (p.Required)
				required.Add(p.Name);
		}

		if (d.Idempotent)
		{
			properties[McpDefaults.IdempotencyKeyArgument] = JsonSerializer.SerializeToElement(new
			{
				type = "string",
				format = McpDefaults.IdempotencyKeyJsonFormat,
				description = McpDefaults.IdempotencyKeyArgumentDescription
			});
			required.Add(McpDefaults.IdempotencyKeyArgument);
		}

		if (d.AllowDryRun)
			properties[McpDefaults.DryRunArgument] = JsonSerializer.SerializeToElement(new { type = "boolean", description = "When true, do not persist writes." });

		if (d.RequireConfirmation)
		{
			properties[McpDefaults.ConfirmedArgument] = JsonSerializer.SerializeToElement(new { type = "boolean", description = "Must be true to execute this write." });
			required.Add(McpDefaults.ConfirmedArgument);
		}

		var schemaObj = new Dictionary<string, object?>
		{
			["type"] = "object",
			["properties"] = properties.ToDictionary(
				kv => kv.Key,
				kv => (object?)JsonSerializer.Deserialize<Dictionary<string, object?>>(kv.Value.GetRawText()) ?? new Dictionary<string, object?>()),
			["required"] = required
		};

		return new Tool
		{
			Name = d.Name,
			Description = d.Description,
			InputSchema = JsonSerializer.SerializeToElement(schemaObj),
			Annotations = new ToolAnnotations
			{
				ReadOnlyHint = d.Kind == McpToolKind.Query,
				DestructiveHint = d.Kind == McpToolKind.Command,
				IdempotentHint = d.Idempotent || d.Kind == McpToolKind.Query
			}
		};
	}

	private static Dictionary<string, object?> ToPropertySchema(McpJsonProperty p)
	{
		var schema = new Dictionary<string, object?> { ["type"] = p.JsonType };
		if (!string.IsNullOrWhiteSpace(p.Description))
			schema["description"] = p.Description;
		if (p.EnumNames is { Count: > 0 })
			schema["enum"] = p.EnumNames;
		else if (p.EnumValues is { Count: > 0 })
			schema["enum"] = p.EnumValues;
		return schema;
	}
}

internal static class ToolCatalogMarkdown
{
	public static string Render(IReadOnlyList<McpToolDescriptor> tools)
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("| Name | Kind | Description | Idempotent | Flag |");
		sb.AppendLine("|------|------|-------------|------------|------|");
		foreach (var t in tools)
			sb.AppendLine($"| `{t.Name}` | {t.Kind} | {t.Description} | {t.Idempotent} | {t.FeatureFlag ?? ""} |");
		return sb.ToString();
	}
}
