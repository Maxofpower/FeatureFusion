using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Mediator.DependencyInjection;

/// <summary>
/// Registers concrete and open-generic command/query handlers from assemblies (no Scrutor).
/// Uses Skip semantics: existing <see cref="ServiceDescriptor.ServiceType"/> registrations are left alone.
/// Open-generic handlers are recorded in <see cref="OpenGenericHandlerRegistry"/> for on-demand closing.
/// </summary>
internal static class HandlerAssemblyScanner
{
	public static OpenGenericHandlerRegistry RegisterHandlers(
		IServiceCollection services,
		Assembly[] assemblies,
		ServiceLifetime handlerLifetime)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(assemblies);

		var openRegistry = new OpenGenericHandlerRegistry();

		foreach (var assembly in assemblies)
		{
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
			}

			foreach (var type in types)
			{
				if (type is null || !IsHandlerCandidate(type))
					continue;

				if (type.IsGenericTypeDefinition)
				{
					RegisterOpenGenericHandler(openRegistry, type);
					continue;
				}

				foreach (var iface in type.GetInterfaces())
				{
					if (!IsClosedHandlerInterface(iface))
						continue;

					services.TryAdd(new ServiceDescriptor(iface, type, handlerLifetime));
				}
			}
		}

		services.TryAddSingleton(openRegistry);
		return openRegistry;
	}

	private static void RegisterOpenGenericHandler(OpenGenericHandlerRegistry registry, Type openHandlerType)
	{
		foreach (var iface in openHandlerType.GetInterfaces())
		{
			if (!iface.IsGenericType)
				continue;

			var definition = iface.GetGenericTypeDefinition();
			if (definition != typeof(ICommandHandler<>)
			    && definition != typeof(ICommandHandler<,>)
			    && definition != typeof(IQueryHandler<,>))
				continue;

			registry.Add(openHandlerType, iface);
		}
	}

	private static bool IsHandlerCandidate(Type type) =>
		type is { IsClass: true, IsAbstract: false }
		&& (type.IsPublic || type.IsNestedPublic);

	private static bool IsClosedHandlerInterface(Type iface)
	{
		if (!iface.IsGenericType || iface.IsGenericTypeDefinition)
			return false;

		var definition = iface.GetGenericTypeDefinition();
		return definition == typeof(ICommandHandler<>)
		       || definition == typeof(ICommandHandler<,>)
		       || definition == typeof(IQueryHandler<,>);
	}
}
