using System.Reflection;
using BuildingBlocks.Mcp.Catalog;
using BuildingBlocks.Mcp.Hosting;
using BuildingBlocks.Mcp.Invocation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Mcp;

/// <summary>
/// Fluent configuration for <see cref="McpServiceCollectionExtensions.AddBuildingBlocksMcp"/>.
/// </summary>
public sealed class McpBuilder
{
	private readonly List<Assembly> _assemblies = [];
	private readonly List<McpToolDescriptor> _mapped = [];
	internal McpTelemetryOptions? TelemetryOptions { get; private set; }
	internal bool StdioTransport { get; private set; }

	/// <summary>The service collection being configured.</summary>
	public IServiceCollection Services { get; }

	internal McpBuilder(IServiceCollection services)
	{
		Services = services;
	}

	/// <summary>Scans <paramref name="assembly"/> for <see cref="McpToolAttribute"/>.</summary>
	public McpBuilder ScanAssembly(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		_assemblies.Add(assembly);
		return this;
	}

	/// <summary>Scans the assembly containing <typeparamref name="T"/>.</summary>
	public McpBuilder ScanAssemblyContaining<T>() => ScanAssembly(typeof(T).Assembly);

	/// <summary>Enables ActivitySource <see cref="McpDefaults.ActivitySourceName"/>.</summary>
	public McpBuilder UseTelemetry(Action<McpTelemetryOptions>? configure = null)
	{
		TelemetryOptions = new McpTelemetryOptions();
		configure?.Invoke(TelemetryOptions);
		return this;
	}

	/// <summary>
	/// Registers in-process <see cref="MemoryIdempotencyStore"/> (single instance). Optional TTL.
	/// Commands require <c>idempotencyKey</c> when this store is registered. Multi-instance hosts should register Redis (or similar) as <see cref="IMcpIdempotencyStore"/> instead.
	/// </summary>
	public McpBuilder UseMemoryIdempotency(TimeSpan? timeToLive = null)
	{
		Services.TryAddSingleton<IMcpIdempotencyStore>(_ => new MemoryIdempotencyStore(timeToLive));
		return this;
	}

	/// <summary>
	/// Registers stdio transport (Claude Desktop / Cursor <c>command</c>). Do not enable on a web API host — stdin is not a JSON-RPC pipe there.
	/// </summary>
	public McpBuilder UseStdioTransport()
	{
		StdioTransport = true;
		return this;
	}

	/// <summary>
	/// Registers a typed tool without <see cref="McpToolAttribute"/> (delegate dispatcher).
	/// </summary>
	public McpBuilder MapTool<TMessage, TResult>(
		string name,
		string description,
		Func<TMessage, McpInvokeContext, CancellationToken, Task<McpResult<TResult>>> handler,
		Action<McpToolAttribute>? configure = null)
		where TMessage : class
	{
		ArgumentNullException.ThrowIfNull(handler);
		return MapToolCore<TMessage, TResult>(
			name,
			description,
			async (_, msg, ctx, ct) => await handler(msg, ctx, ct).ConfigureAwait(false),
			createScope: false,
			configure);
	}

	/// <summary>
	/// Registers a typed tool whose handler receives a <strong>scoped</strong> <see cref="IServiceProvider"/>
	/// (API / service hosts that resolve validators, feature flags, etc.).
	/// </summary>
	public McpBuilder MapTool<TMessage, TResult>(
		string name,
		string description,
		Func<IServiceProvider, TMessage, McpInvokeContext, CancellationToken, Task<McpResult<TResult>>> handler,
		Action<McpToolAttribute>? configure = null)
		where TMessage : class
	{
		ArgumentNullException.ThrowIfNull(handler);
		return MapToolCore(name, description, handler, createScope: true, configure);
	}

	private McpBuilder MapToolCore<TMessage, TResult>(
		string name,
		string description,
		Func<IServiceProvider, TMessage, McpInvokeContext, CancellationToken, Task<McpResult<TResult>>> handler,
		bool createScope,
		Action<McpToolAttribute>? configure)
		where TMessage : class
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(description);

		var attr = new McpToolAttribute(name) { Description = description, Kind = McpToolKind.Command };
		configure?.Invoke(attr);
		if (attr.Kind == McpToolKind.Unspecified)
			attr.Kind = McpToolKind.Command;

		_mapped.Add(McpToolScanner.Create(
			attr.Name,
			attr.Description,
			typeof(TMessage),
			attr.Kind,
			attr.Idempotent,
			attr.AllowDryRun,
			attr.RequireConfirmation,
			attr.TimeoutMilliseconds > 0 ? TimeSpan.FromMilliseconds(attr.TimeoutMilliseconds) : null,
			string.IsNullOrWhiteSpace(attr.FeatureFlag) ? null : attr.FeatureFlag,
			attr.Roles ?? [],
			async (root, msg, ctx, ct) =>
			{
				if (!createScope)
				{
					var typed = await handler(root, (TMessage)msg, ctx, ct).ConfigureAwait(false);
					return typed.IsSuccess ? McpResult.Ok<object?>(typed.Value) : McpResult.Fail<object?>(typed.Error!);
				}

				var factory = root.GetRequiredService<IServiceScopeFactory>();
				await using var scope = factory.CreateAsyncScope();
				var typedScoped = await handler(scope.ServiceProvider, (TMessage)msg, ctx, ct).ConfigureAwait(false);
				return typedScoped.IsSuccess
					? McpResult.Ok<object?>(typedScoped.Value)
					: McpResult.Fail<object?>(typedScoped.Error!);
			}));
		return this;
	}

	/// <summary>
	/// Uses a delegate as <see cref="IMcpMessageDispatcher"/> (no Mediator package required).
	/// </summary>
	public McpBuilder UseDispatcher(Func<IServiceProvider, object, CancellationToken, Task<object?>> dispatcher)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		Services.AddSingleton<IMcpMessageDispatcher>(sp => new DelegateMessageDispatcher(sp, dispatcher));
		return this;
	}

	internal IReadOnlyList<McpToolDescriptor> BuildCatalog(IReadOnlyList<McpToolDescriptor>? endpointTools = null)
	{
		var list = new List<McpToolDescriptor>(_mapped);
		foreach (var assembly in _assemblies)
			list.AddRange(McpToolScanner.Scan(assembly));
		return McpToolScanner.MergePreferringFirst(list, endpointTools ?? []);
	}
}

internal sealed class DelegateMessageDispatcher : IMcpMessageDispatcher
{
	private readonly IServiceProvider _sp;
	private readonly Func<IServiceProvider, object, CancellationToken, Task<object?>> _dispatcher;

	public DelegateMessageDispatcher(IServiceProvider sp, Func<IServiceProvider, object, CancellationToken, Task<object?>> dispatcher)
	{
		_sp = sp;
		_dispatcher = dispatcher;
	}

	public Task<object?> SendAsync(object message, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(message);
		return _dispatcher(_sp, message, cancellationToken);
	}
}

/// <summary>
/// DI entry point for BuildingBlocks.Mcp.
/// </summary>
/// <remarks>
/// Registers an immutable catalog, <see cref="IMcpInvoker"/>, and official MCP server handlers
/// (list/call tools + <c>catalog://tools</c>). Call MapBuildingBlocksMcp for HTTP.
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddBuildingBlocksMcp(o =>
/// {
///     o.ScanAssemblyContaining&lt;CreateOrder&gt;();
///     o.UseTelemetry();
/// }).UseDispatcher((sp, msg, ct) =&gt; Task.FromResult&lt;object?&gt;(null));
/// </code>
/// </example>
public static class McpServiceCollectionExtensions
{
	/// <summary>
	/// Adds BuildingBlocks.Mcp services and official MCP server handlers.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
	/// <exception cref="McpCatalogException">Invalid or duplicate tools when the catalog is built.</exception>
	public static McpBuilder AddBuildingBlocksMcp(this IServiceCollection services, Action<McpBuilder>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var builder = new McpBuilder(services);
		configure?.Invoke(builder);

		services.AddHttpContextAccessor();
		services.TryAddSingleton<McpEndpointToolRegistry>();
		services.TryAddSingleton<McpRouteSourceHolder>();
		services.AddSingleton(sp =>
		{
			TryBuildMappedEndpoints(sp);
			var extra = sp.GetRequiredService<McpEndpointToolRegistry>().Snapshot();
			var catalog = builder.BuildCatalog(extra);
			if (catalog.Count == 0)
				throw new McpCatalogException("No MCP tools registered. Scan an assembly, call MapTool, or WithMcp.");
			return catalog;
		});

		services.TryAddSingleton<IMcpResultMapper, DefaultMcpResultMapper>();
		services.TryAddSingleton<IMcpRateLimiter, NoOpRateLimiter>();
		services.TryAddSingleton<McpInvokeContextAccessor>();
		services.TryAddSingleton<IMcpInvokeContextAccessor>(sp => sp.GetRequiredService<McpInvokeContextAccessor>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IMcpToolFilter, FeatureFlagCallbackFilter>());
		services.AddSingleton<FeatureFlagCallbackOptions>();

		if (builder.TelemetryOptions is not null)
			services.AddSingleton(new McpTelemetry(builder.TelemetryOptions));

		services.AddSingleton<IMcpInvoker>(sp =>
		{
			var catalog = sp.GetRequiredService<IReadOnlyList<McpToolDescriptor>>();
			var telemetry = sp.GetService<McpTelemetry>();
			return new McpInvoker(
				catalog,
				sp,
				sp.GetServices<IMcpToolFilter>(),
				sp.GetRequiredService<IMcpRateLimiter>(),
				sp.GetRequiredService<IMcpResultMapper>(),
				sp.GetService<IMcpMessageDispatcher>(),
				sp.GetService<IMcpIdempotencyStore>(),
				sp.GetService<IMcpResiliencePolicy>(),
				telemetry,
				telemetry?.IncludeExceptionDetails == true);
		});

		Protocol.McpProtocolRegistration.AddMcpServer(services, builder.StdioTransport);
		return builder;
	}

	/// <summary>
	/// Runs Minimal API conventions so <c>WithMcp</c> can register tools. Failures on unrelated routes must not hide Scan/MapTool.
	/// </summary>
	private static void TryBuildMappedEndpoints(IServiceProvider sp)
	{
		try
		{
			var holder = sp.GetService<McpRouteSourceHolder>();
			if (holder?.Routes is not null)
			{
				foreach (var source in holder.Routes.DataSources)
				{
					try
					{
						_ = source.Endpoints;
					}
					catch (Exception)
					{
						// RequestDelegateFactory can reject HTTP-only signatures; Scan/MapTool catalog still applies.
					}
				}

				return;
			}

			if (sp.GetService<EndpointDataSource>() is { } fallback)
				_ = fallback.Endpoints;
		}
		catch (Exception)
		{
			// Catalog still built from scanned types and MapTool.
		}
	}

	/// <summary>
	/// Registers a callback used by the built-in feature-flag filter when <see cref="McpToolAttribute.FeatureFlag"/> is set.
	/// </summary>
	public static IServiceCollection AddMcpFeatureFlagEvaluator(
		this IServiceCollection services,
		Func<string, McpInvokeContext, CancellationToken, ValueTask<bool>> evaluator)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(evaluator);
		services.AddSingleton(new FeatureFlagCallbackOptions { Evaluator = evaluator });
		return services;
	}
}

/// <summary>Holds an optional feature-flag evaluator.</summary>
public sealed class FeatureFlagCallbackOptions
{
	/// <summary>When null, flags do not hide tools.</summary>
	public Func<string, McpInvokeContext, CancellationToken, ValueTask<bool>>? Evaluator { get; set; }
}

internal sealed class FeatureFlagCallbackFilter : IMcpToolFilter
{
	private readonly FeatureFlagCallbackOptions _options;

	public FeatureFlagCallbackFilter(FeatureFlagCallbackOptions options)
	{
		_options = options;
	}

	public async ValueTask<bool> IsVisibleAsync(McpToolDescriptor tool, McpInvokeContext context, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(tool.FeatureFlag) || _options.Evaluator is null)
			return true;
		return await _options.Evaluator(tool.FeatureFlag, context, cancellationToken).ConfigureAwait(false);
	}
}
