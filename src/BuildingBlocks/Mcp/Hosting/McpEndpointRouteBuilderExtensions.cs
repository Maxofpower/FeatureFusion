using System.Reflection;
using BuildingBlocks.Mcp.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore;

namespace BuildingBlocks.Mcp.Hosting;

/// <summary>
/// Maps Streamable HTTP MCP using the official SDK, and optional <c>WithMcp</c> on Minimal APIs.
/// MVC controllers are unsupported for now.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Maps MCP at <paramref name="pattern"/> (default <see cref="McpDefaults.HttpPath"/>).
	/// </summary>
	/// <remarks>
	/// The ASP.NET Core host must already be running for Cursor HTTP configs that use a URL.
	/// Honor <c>HttpContext.RequestAborted</c> via the SDK pipeline.
	/// Map Minimal API <c>WithMcp</c> before the first tool list/call (catalog is built lazily).
	/// </remarks>
	public static IEndpointConventionBuilder MapBuildingBlocksMcp(this IEndpointRouteBuilder endpoints, string? pattern = null)
	{
		ArgumentNullException.ThrowIfNull(endpoints);
		if (endpoints.ServiceProvider.GetService<McpRouteSourceHolder>() is { } holder)
			holder.Routes = endpoints;
		return endpoints.MapMcp(string.IsNullOrWhiteSpace(pattern) ? McpDefaults.HttpPath : pattern);
	}

	/// <summary>
	/// Registers this Minimal API handler as an MCP tool using <see cref="McpToolAttribute"/> on the method.
	/// Pass the same <see cref="IEndpointRouteBuilder"/> used for <c>MapGet</c>/<c>MapPost</c>.
	/// MVC controllers are unsupported for now.
	/// </summary>
	public static RouteHandlerBuilder WithMcp(this RouteHandlerBuilder builder, IEndpointRouteBuilder endpoints)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(endpoints);
		Attach(endpoints, builder, name: null, description: null, configure: null);
		return builder;
	}

	/// <summary>
	/// Registers this public static Minimal API method as an MCP tool without requiring <see cref="McpToolAttribute"/>.
	/// GET → query (no idempotency key). POST/PUT → command (idempotent writes). MVC controllers are unsupported for now.
	/// </summary>
	public static RouteHandlerBuilder WithMcp(
		this RouteHandlerBuilder builder,
		IEndpointRouteBuilder endpoints,
		string name,
		string description,
		Action<McpToolAttribute>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(endpoints);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(description);
		Attach(endpoints, builder, name, description, configure);
		return builder;
	}

	private static void Attach(
		IEndpointRouteBuilder endpoints,
		RouteHandlerBuilder builder,
		string? name,
		string? description,
		Action<McpToolAttribute>? configure)
	{
		if (endpoints.ServiceProvider.GetService<McpRouteSourceHolder>() is { } holder)
			holder.Routes = endpoints;
		var sp = endpoints.ServiceProvider;
		builder.Finally(eb => Register(eb, sp, name, description, configure));
	}

	private static void Register(
		EndpointBuilder endpointBuilder,
		IServiceProvider services,
		string? name,
		string? description,
		Action<McpToolAttribute>? configure)
	{
		var method = endpointBuilder.Metadata.OfType<MethodInfo>().FirstOrDefault()
			?? throw new McpCatalogException(
				"WithMcp requires a public static Minimal API method-group. MVC controllers are unsupported for now.");

		McpToolAttribute attr;
		if (name is null)
		{
			attr = method.GetCustomAttribute<McpToolAttribute>()
				?? throw new McpCatalogException(
					$"WithMcp() on '{method.DeclaringType?.Name}.{method.Name}' requires [{nameof(McpToolAttribute)}] or WithMcp(name, description).");
		}
		else
		{
			attr = new McpToolAttribute(name) { Description = description! };
			configure?.Invoke(attr);
		}

		var descriptor = McpMethodTool.FromMethod(method, attr, InferKindFromHttp(endpointBuilder));
		services.GetRequiredService<McpEndpointToolRegistry>().Add(descriptor);
	}

	private static McpToolKind InferKindFromHttp(EndpointBuilder endpointBuilder)
	{
		var methods = endpointBuilder.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods;
		if (methods is null || methods.Count == 0)
			return McpToolKind.Unspecified;
		if (methods.All(m => string.Equals(m, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)))
			return McpToolKind.Query;
		if (methods.All(m =>
			    string.Equals(m, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
			    || string.Equals(m, HttpMethods.Put, StringComparison.OrdinalIgnoreCase)))
			return McpToolKind.Command;
		return McpToolKind.Unspecified;
	}
}

/// <summary>
/// Stdio host helper for Claude Desktop and Cursor command-based servers.
/// </summary>
/// <remarks>
/// Log only to stderr. Stdout is the JSON-RPC pipe.
/// </remarks>
public static class McpHostBuilderExtensions
{
	/// <summary>
	/// Prefer <see cref="McpBuilder.UseStdioTransport"/> on <c>AddBuildingBlocksMcp</c> for Claude Desktop / Cursor command hosts.
	/// </summary>
	public static IServiceCollection AddBuildingBlocksMcpStdio(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		return services;
	}

	/// <summary>
	/// Runs a generic host with stdio MCP. Intended for console samples.
	/// </summary>
	public static Task RunBuildingBlocksMcpStdioAsync(this IHost host, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host.RunAsync(cancellationToken);
	}
}
