using System.Reflection;
using BuildingBlocks.Mediator.Implementation;
using BuildingBlocks.Mediator.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Mediator.DependencyInjection;

/// <summary>
/// Configuration for <see cref="MediatorServiceCollectionExtensions.AddMediator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Lifetime"/> defaults to <see cref="ServiceLifetime.Scoped"/>. Prefer Scoped so
/// <see cref="ISender"/> aligns with typical request scopes. Registering as Singleton can create
/// captive dependencies when handlers or behaviors resolve scoped services.
/// </para>
/// <para>
/// <see cref="UseTelemetry"/> is optional. When enabled it registers an ActivitySource
/// (name configurable) and, unless metrics are disabled, a Meter, and wraps each Send around
/// the full pipeline + handler — it does <strong>not</strong> add a pipeline behavior.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddMediator(cfg =&gt;
/// {
///     cfg.RegisterServicesFromAssemblyContaining&lt;CreateOrderHandler&gt;();
///     cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;), order: 0);
///     cfg.UseTelemetry(o =&gt; o.ActivitySourceName = "BuildingBlocks.Mediator");
///     cfg.ValidateOnStartup = true;
/// });
/// </code>
/// </example>
public sealed class MediatorConfiguration
{
	private int _nextRegistrationIndex;

	internal List<Assembly> Assemblies { get; } = new();
	internal List<BehaviorRegistration> BehaviorsToRegister { get; } = new();
	internal MediatorTelemetryOptions? TelemetryOptions { get; private set; }

	/// <summary>
	/// Lifetime for <see cref="ISender"/> / <see cref="IMediator"/>. Default is <see cref="ServiceLifetime.Scoped"/>.
	/// </summary>
	public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;

	/// <summary>
	/// Lifetime for discovered command/query handlers. Default is <see cref="ServiceLifetime.Transient"/>.
	/// Prefer Transient or Scoped; Singleton handlers that depend on scoped services create captive dependencies.
	/// Open-generic handlers always resolve as Transient via ActivatorUtilities regardless of this setting.
	/// </summary>
	public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

	/// <summary>
	/// When true, <see cref="MediatorServiceCollectionExtensions.AddMediator"/> validates that every
	/// concrete command/query in registered assemblies has exactly one matching handler
	/// (runs at host registration / startup — not at compile time).
	/// </summary>
	public bool ValidateOnStartup { get; set; }

	/// <summary>Scans <paramref name="assembly"/> for command/query handlers.</summary>
	public MediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		if (!Assemblies.Contains(assembly))
			Assemblies.Add(assembly);
		return this;
	}

	/// <summary>Scans the assembly containing <typeparamref name="T"/>.</summary>
	public MediatorConfiguration RegisterServicesFromAssemblyContaining<T>()
		=> RegisterServicesFromAssembly(typeof(T).Assembly);

	/// <summary>
	/// Enables optional Send enrichment via a configurable <see cref="System.Diagnostics.ActivitySource"/>
	/// and, unless <see cref="MediatorTelemetryOptions.EnableMetrics"/> is false, a
	/// <see cref="System.Diagnostics.Metrics.Meter"/>.
	/// Activity wraps the full pipeline + handler (not a pipeline behavior). Omit this call for zero telemetry overhead.
	/// </summary>
	/// <param name="configure">Optional; set <see cref="MediatorTelemetryOptions.ActivitySourceName"/>, meter, logging, and exception flags.</param>
	public MediatorConfiguration UseTelemetry(Action<MediatorTelemetryOptions>? configure = null)
	{
		var options = new MediatorTelemetryOptions();
		configure?.Invoke(options);
		if (string.IsNullOrWhiteSpace(options.ActivitySourceName))
			throw new ArgumentException("ActivitySourceName must be a non-empty string when UseTelemetry is enabled.", nameof(configure));
		if (string.IsNullOrWhiteSpace(options.MeterName))
			options.MeterName = options.ActivitySourceName;
		TelemetryOptions = options;
		return this;
	}

	/// <summary>
	/// Registers a closed pipeline behavior (or a concrete type implementing a closed <see cref="IPipelineBehavior{TRequest,TResponse}"/>).
	/// </summary>
	public MediatorConfiguration AddBehavior<TBehavior>()
		where TBehavior : class
		=> AddBehavior<TBehavior>(order: null);

	/// <summary>
	/// Registers a closed pipeline behavior with an explicit pipeline <paramref name="order"/> (lower = outermost).
	/// </summary>
	public MediatorConfiguration AddBehavior<TBehavior>(int order)
		where TBehavior : class
		=> AddBehavior<TBehavior>((int?)order);

	private MediatorConfiguration AddBehavior<TBehavior>(int? order)
		where TBehavior : class
	{
		var index = _nextRegistrationIndex++;
		var effectiveOrder = order ?? index;

		BehaviorsToRegister.Add(new BehaviorRegistration(
			new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), ServiceLifetime.Transient),
			effectiveOrder,
			index));

		foreach (var iface in typeof(TBehavior).GetInterfaces()
			         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
		{
			BehaviorsToRegister.Add(new BehaviorRegistration(
				new ServiceDescriptor(iface, sp => sp.GetRequiredService<TBehavior>(), ServiceLifetime.Transient),
				effectiveOrder,
				index));
		}

		return this;
	}

	/// <summary>
	/// Registers an open-generic pipeline behavior, e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>,
	/// against <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
	/// When order is omitted, registration order is used (first registered = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenBehavior(
		Type openBehaviorType,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenBehavior(openBehaviorType, order: null, serviceLifetime);

	/// <summary>
	/// Registers an open-generic pipeline behavior with an explicit <paramref name="order"/> (lower = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenBehavior(
		Type openBehaviorType,
		int order,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenBehavior(openBehaviorType, (int?)order, serviceLifetime);

	internal MediatorConfiguration AddOpenBehavior(
		Type openBehaviorType,
		int? order,
		ServiceLifetime serviceLifetime)
	{
		EnsureOpenPipelineBehavior(openBehaviorType, typeof(IPipelineBehavior<,>), nameof(openBehaviorType));

		var index = _nextRegistrationIndex++;
		var effectiveOrder = order ?? index;

		BehaviorsToRegister.Add(new BehaviorRegistration(
			new ServiceDescriptor(
				typeof(IPipelineBehavior<,>),
				openBehaviorType,
				serviceLifetime),
			effectiveOrder,
			index));

		return this;
	}

	/// <summary>
	/// Registers an open-generic command-only pipeline behavior
	/// (must implement <see cref="Pipeline.ICommandPipelineBehavior{TCommand,TResponse}"/>).
	/// When order is omitted, registration order is used (first registered = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenCommandBehavior(
		Type openBehaviorType,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenCommandBehavior(openBehaviorType, order: null, serviceLifetime);

	/// <summary>
	/// Registers an open-generic command-only pipeline behavior with an explicit
	/// <paramref name="order"/> (lower = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenCommandBehavior(
		Type openBehaviorType,
		int order,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenCommandBehavior(openBehaviorType, (int?)order, serviceLifetime);

	internal MediatorConfiguration AddOpenCommandBehavior(
		Type openBehaviorType,
		int? order,
		ServiceLifetime serviceLifetime)
	{
		EnsureOpenPipelineBehavior(
			openBehaviorType,
			typeof(Pipeline.ICommandPipelineBehavior<,>),
			nameof(openBehaviorType),
			"ICommandPipelineBehavior");
		return AddOpenBehavior(openBehaviorType, order, serviceLifetime);
	}

	/// <summary>
	/// Registers an open-generic query-only pipeline behavior
	/// (must implement <see cref="Pipeline.IQueryPipelineBehavior{TQuery,TResponse}"/>).
	/// When order is omitted, registration order is used (first registered = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenQueryBehavior(
		Type openBehaviorType,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenQueryBehavior(openBehaviorType, order: null, serviceLifetime);

	/// <summary>
	/// Registers an open-generic query-only pipeline behavior with an explicit
	/// <paramref name="order"/> (lower = outermost).
	/// </summary>
	public MediatorConfiguration AddOpenQueryBehavior(
		Type openBehaviorType,
		int order,
		ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
		=> AddOpenQueryBehavior(openBehaviorType, (int?)order, serviceLifetime);

	internal MediatorConfiguration AddOpenQueryBehavior(
		Type openBehaviorType,
		int? order,
		ServiceLifetime serviceLifetime)
	{
		EnsureOpenPipelineBehavior(
			openBehaviorType,
			typeof(Pipeline.IQueryPipelineBehavior<,>),
			nameof(openBehaviorType),
			"IQueryPipelineBehavior");
		return AddOpenBehavior(openBehaviorType, order, serviceLifetime);
	}

	private static void EnsureOpenPipelineBehavior(
		Type openBehaviorType,
		Type requiredOpenInterface,
		string paramName,
		string? requiredInterfaceDisplayName = null)
	{
		ArgumentNullException.ThrowIfNull(openBehaviorType);

		if (!openBehaviorType.IsGenericTypeDefinition)
			throw new ArgumentException(
				$"{openBehaviorType.Name} must be an open generic type definition (e.g. MyBehavior&lt;,&gt;).",
				paramName);

		var implements = openBehaviorType.GetInterfaces()
			.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == requiredOpenInterface);

		if (!implements)
		{
			var iface = requiredInterfaceDisplayName ?? "IPipelineBehavior";
			throw new ArgumentException(
				$"{openBehaviorType.Name} must implement {iface}&lt;,&gt;.",
				paramName);
		}
	}
}

/// <summary>Ordered behavior registration entry.</summary>
internal readonly record struct BehaviorRegistration(
	ServiceDescriptor Descriptor,
	int Order,
	int RegistrationIndex);

/// <summary>DI registration for BuildingBlocks.Mediator.</summary>
public static class MediatorServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="ISender"/>, <see cref="IMediator"/>, command/query handlers, and pipeline behaviors.
	/// </summary>
	/// <remarks>
	/// Handlers are discovered via a built-in assembly scanner (no Scrutor dependency).
	/// Behaviors are sorted by order ascending (lower = outermost), then registration index.
	/// Call <see cref="MediatorConfiguration.UseTelemetry"/> to optionally enrich Send with an ActivitySource.
	/// </remarks>
	public static IServiceCollection AddMediator(
		this IServiceCollection services,
		Action<MediatorConfiguration> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var configuration = new MediatorConfiguration();
		configure(configuration);

		if (configuration.Assemblies.Count == 0)
			throw new InvalidOperationException(
				"AddMediator requires at least one assembly via RegisterServicesFromAssembly.");

		var assemblies = configuration.Assemblies.ToArray();

		var openRegistry = HandlerAssemblyScanner.RegisterHandlers(
			services,
			assemblies,
			configuration.HandlerLifetime);

		services.TryAddSingleton(HandlerCardinalityIndex.Create(services));

		if (configuration.TelemetryOptions is not null)
		{
			services.AddSingleton(Options.Create(configuration.TelemetryOptions));
			// Hosts normally already have ILoggerFactory; fall back so UseTelemetry works in minimal hosts.
			services.TryAddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
			services.TryAddSingleton<MediatorSendTelemetry>();
		}

		foreach (var behavior in configuration.BehaviorsToRegister
			         .OrderBy(b => b.Order)
			         .ThenBy(b => b.RegistrationIndex))
		{
			services.TryAddEnumerable(behavior.Descriptor);
		}

		services.TryAdd(new ServiceDescriptor(
			typeof(global::BuildingBlocks.Mediator.Mediator),
			sp => new global::BuildingBlocks.Mediator.Mediator(
				sp,
				sp.GetService<MediatorSendTelemetry>()),
			configuration.Lifetime));

		services.TryAdd(new ServiceDescriptor(
			typeof(ISender),
			sp => sp.GetRequiredService<global::BuildingBlocks.Mediator.Mediator>(),
			configuration.Lifetime));

		services.TryAdd(new ServiceDescriptor(
			typeof(IMediator),
			sp => sp.GetRequiredService<global::BuildingBlocks.Mediator.Mediator>(),
			configuration.Lifetime));

		if (configuration.ValidateOnStartup)
			MediatorHandlerValidator.Validate(services, assemblies, openRegistry);

		return services;
	}
}

internal static class MediatorHandlerValidator
{
	public static void Validate(
		IServiceCollection services,
		Assembly[] assemblies,
		OpenGenericHandlerRegistry openRegistry)
	{
		var errors = new List<string>();

		var messageTypes = assemblies
			.SelectMany(SafeGetTypes)
			.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
			            && (t.IsPublic || t.IsNestedPublic))
			.ToList();

		foreach (var type in messageTypes)
		{
			if (typeof(ICommand).IsAssignableFrom(type))
			{
				var handlerType = typeof(ICommandHandler<>).MakeGenericType(type);
				var count = Count(services, handlerType, openRegistry);
				if (count == 0)
					errors.Add($"Missing ICommandHandler<{type.Name}> for void command '{type.FullName}'.");
				else if (count > 1)
					errors.Add($"Multiple ({count}) ICommandHandler<{type.Name}> registrations for '{type.FullName}'.");
				continue;
			}

			var commandWithResponse = type.GetInterfaces()
				.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
			if (commandWithResponse is not null)
			{
				var response = commandWithResponse.GetGenericArguments()[0];
				var handlerType = typeof(ICommandHandler<,>).MakeGenericType(type, response);
				var count = Count(services, handlerType, openRegistry);
				if (count == 0)
					errors.Add($"Missing ICommandHandler<{type.Name}, {response.Name}> for '{type.FullName}'.");
				else if (count > 1)
					errors.Add($"Multiple ({count}) ICommandHandler<{type.Name}, {response.Name}> for '{type.FullName}'.");
				continue;
			}

			var query = type.GetInterfaces()
				.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
			if (query is not null)
			{
				var response = query.GetGenericArguments()[0];
				var handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, response);
				var count = Count(services, handlerType, openRegistry);
				if (count == 0)
					errors.Add($"Missing IQueryHandler<{type.Name}, {response.Name}> for '{type.FullName}'.");
				else if (count > 1)
					errors.Add($"Multiple ({count}) IQueryHandler<{type.Name}, {response.Name}> for '{type.FullName}'.");
			}
		}

		if (errors.Count > 0)
		{
			throw new InvalidOperationException(
				"BuildingBlocks.Mediator ValidateOnStartup failed:" + Environment.NewLine +
				string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
		}
	}

	private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(t => t is not null)!;
		}
	}

	private static int Count(
		IServiceCollection services,
		Type serviceType,
		OpenGenericHandlerRegistry openRegistry)
	{
		var closed = services.Count(d => d.ServiceType == serviceType);
		if (closed > 0)
			return closed;

		return openRegistry.CanSatisfy(serviceType) ? 1 : 0;
	}
}
