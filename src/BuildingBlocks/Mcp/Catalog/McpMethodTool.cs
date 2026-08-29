using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Mcp.Catalog;

/// <summary>
/// Binds JSON message + DI into a public static method marked <see cref="McpToolAttribute"/>.
/// </summary>
internal static class McpMethodTool
{
	internal sealed class EmptyArguments
	{
	}

	public static McpToolDescriptor FromMethod(MethodInfo method)
	{
		ArgumentNullException.ThrowIfNull(method);
		var attr = method.GetCustomAttribute<McpToolAttribute>()
			?? throw new McpCatalogException($"Method '{method.DeclaringType?.FullName}.{method.Name}' is missing [{nameof(McpToolAttribute)}].");
		return FromMethod(method, attr, kindHint: McpToolKind.Unspecified);
	}

	public static McpToolDescriptor FromMethod(MethodInfo method, McpToolAttribute attr, McpToolKind kindHint)
	{
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(attr);

		if (!method.IsStatic || method.IsGenericMethodDefinition)
			throw new McpCatalogException($"MCP tool '{Format(method)}' requires a public static non-generic method.");

		if (string.IsNullOrWhiteSpace(attr.Description))
			throw new McpCatalogException($"Tool '{attr.Name}' on '{Format(method)}' requires {nameof(McpToolAttribute.Description)}.");

		var messageType = ResolveMessageType(method);
		var kind = attr.Kind != McpToolKind.Unspecified
			? attr.Kind
			: kindHint != McpToolKind.Unspecified
				? kindHint
				: McpToolScanner.InferKind(messageType);
		if (kind == McpToolKind.Unspecified)
			throw new McpCatalogException($"Tool '{attr.Name}' on '{Format(method)}' must set {nameof(McpToolAttribute.Kind)} (or map GET/POST/PUT).");

		TimeSpan? timeout = attr.TimeoutMilliseconds > 0 ? TimeSpan.FromMilliseconds(attr.TimeoutMilliseconds) : null;

		return McpToolScanner.Create(
			attr.Name,
			attr.Description.Trim(),
			messageType,
			kind,
			attr.Idempotent,
			attr.AllowDryRun,
			attr.RequireConfirmation,
			timeout,
			string.IsNullOrWhiteSpace(attr.FeatureFlag) ? null : attr.FeatureFlag.Trim(),
			attr.Roles ?? [],
			(root, msg, ctx, ct) => InvokeAsync(method, messageType, root, msg, ctx, ct));
	}

	private static Type ResolveMessageType(MethodInfo method)
	{
		var body = method.GetParameters().Where(p => !IsInjected(p)).ToArray();
		if (body.Length == 0)
			return typeof(EmptyArguments);
		if (body.Length == 1)
			return body[0].ParameterType;
		throw new McpCatalogException(
			$"[{nameof(McpToolAttribute)}] on '{Format(method)}' allows one JSON request parameter; remaining parameters must be DI services, {nameof(CancellationToken)}, or {nameof(McpInvokeContext)}.");
	}

	internal static bool IsInjected(ParameterInfo parameter)
	{
		var t = parameter.ParameterType;
		if (t == typeof(CancellationToken) || t == typeof(McpInvokeContext) || t == typeof(IServiceProvider))
			return true;
		if (typeof(ILogger).IsAssignableFrom(t) || (t.IsGenericType && t.GetGenericTypeDefinition().Name == "ILogger`1"))
			return true;
		if (t.FullName is "Microsoft.AspNetCore.Http.HttpContext")
			return true;
		if (t.IsInterface)
			return true;
		return false;
	}

	private static async Task<McpResult<object?>> InvokeAsync(
		MethodInfo method,
		Type messageType,
		IServiceProvider root,
		object message,
		McpInvokeContext context,
		CancellationToken cancellationToken)
	{
		var factory = root.GetRequiredService<IServiceScopeFactory>();
		await using var scope = factory.CreateAsyncScope();
		var sp = scope.ServiceProvider;
		var parameters = method.GetParameters();
		var args = new object?[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
		{
			var p = parameters[i];
			if (p.ParameterType == typeof(CancellationToken))
				args[i] = cancellationToken;
			else if (p.ParameterType == typeof(McpInvokeContext))
				args[i] = context;
			else if (p.ParameterType == typeof(IServiceProvider))
				args[i] = sp;
			else if (p.ParameterType == messageType)
				args[i] = message;
			else if (p.ParameterType.FullName is "Microsoft.AspNetCore.Http.HttpContext")
				args[i] = sp.GetService(p.ParameterType);
			else
				args[i] = sp.GetRequiredService(p.ParameterType);
		}

		object? raw;
		try
		{
			raw = method.Invoke(null, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}

		raw = await UnwrapTask(raw).ConfigureAwait(false);
		return new DefaultMcpResultMapper().Map(raw);
	}

	private static async Task<object?> UnwrapTask(object? raw)
	{
		if (raw is null)
			return null;
		if (raw is Task task)
		{
			await task.ConfigureAwait(false);
			var t = task.GetType();
			if (t.IsGenericType)
				return t.GetProperty("Result")!.GetValue(task);
			return null;
		}

		return raw;
	}

	private static string Format(MethodInfo method)
		=> $"{method.DeclaringType?.FullName}.{method.Name}";
}
