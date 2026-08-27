using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class RegistrationValidationTests
{
	[Fact]
	public void ValidateOnStartup_MissingHandler_Throws()
	{
		var services = new ServiceCollection();
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		services.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
		services.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		services.AddTransient<IQueryHandler<TokenProbe, bool>, TokenProbeHandler>();
		services.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
		services.AddTransient<IQueryHandler<ListProducts, Result<PagedResult<string>>>, ListProductsHandler>();
		services.AddTransient<IQueryHandler<ListIds, IReadOnlyList<int>>, ListIdsHandler>();
		services.AddTransient<ICommandHandler<FixedPing, string>, FixedPingClosedHandler>();
		services.AddSingleton(new HandlerState());

		var ex = Assert.Throws<InvalidOperationException>(() =>
			services.AddMediator(cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(GhostCommand).Assembly);
				cfg.ValidateOnStartup = true;
			}));

		Assert.Contains(nameof(GhostCommand), ex.Message);
		Assert.Contains("Missing", ex.Message);
	}

	[Fact]
	public void ValidateOnStartup_WithHandlersPresent_Succeeds()
	{
		var services = new ServiceCollection();
		RegisterAllFixtureHandlers(services);
		services.AddTransient<ICommandHandler<GhostCommand, int>, GhostCommandHandlerForValidation>();

		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly);
			cfg.ValidateOnStartup = true;
		});
	}

	[Fact]
	public void ValidateOnStartup_DetectsDuplicateHandlers()
	{
		var services = new ServiceCollection();
		RegisterAllFixtureHandlers(services);
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, AlternateCreateOrderHandler>();
		services.AddTransient<ICommandHandler<GhostCommand, int>, GhostCommandHandlerForValidation>();

		var ex = Assert.Throws<InvalidOperationException>(() =>
			services.AddMediator(cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly);
				cfg.ValidateOnStartup = true;
			}));

		Assert.Contains("Multiple", ex.Message);
		Assert.Contains(nameof(CreateOrder), ex.Message);
	}

	[Fact]
	public void DuplicateHandlers_AreAllReturnedByGetServices()
	{
		var services = new ServiceCollection();
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, AlternateCreateOrderHandler>();
		services.AddScoped<Mediator>();
		services.AddScoped<ISender>(sp => sp.GetRequiredService<Mediator>());

		using var sp = services.BuildServiceProvider();
		Assert.True(sp.GetServices<ICommandHandler<CreateOrder, OrderResult>>().Count() >= 2);
	}

	[Fact]
	public void AddOpenBehavior_ClosedType_Throws()
	{
		Assert.Throws<ArgumentException>(() =>
			new MediatorConfiguration().AddOpenBehavior(typeof(RecordingBehavior)));
	}

	[Fact]
	public void AddMediator_WithoutAssembly_Throws()
	{
		var services = new ServiceCollection();
		var ex = Assert.Throws<InvalidOperationException>(() =>
			services.AddMediator(_ => { }));
		Assert.Contains("RegisterServicesFromAssembly", ex.Message);
	}

	[Fact]
	public void ValidateOnStartup_OrphanMessage_FailsAtAddMediator_NotAtCompileTime()
	{
		// Same failure mode as FeatureFusion Program.cs with ValidateOnStartup = true:
		// dotnet build succeeds; InvalidOperationException is thrown when AddMediator runs (host startup / WebApplicationFactory).
		var services = new ServiceCollection();
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		services.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
		services.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		services.AddTransient<IQueryHandler<TokenProbe, bool>, TokenProbeHandler>();
		services.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
		services.AddTransient<IQueryHandler<ListProducts, Result<PagedResult<string>>>, ListProductsHandler>();
		services.AddTransient<IQueryHandler<ListIds, IReadOnlyList<int>>, ListIdsHandler>();
		services.AddTransient<ICommandHandler<GhostCommand, int>, GhostCommandHandlerForValidation>();
		services.AddTransient<ICommandHandler<FixedPing, string>, FixedPingClosedHandler>();
		services.AddSingleton(new HandlerState());

		var ex = Assert.Throws<InvalidOperationException>(() =>
			services.AddMediator(cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(OrphanQueryWithoutHandler).Assembly);
				cfg.ValidateOnStartup = true;
			}));

		Assert.Contains(nameof(OrphanQueryWithoutHandler), ex.Message);
		Assert.Contains("Missing", ex.Message);
	}

	[Fact]
	public void ValidateOnStartup_False_AllowsMissingHandlersAtRegistration()
	{
		var services = new ServiceCollection();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.ValidateOnStartup = false;
		});
		// GhostCommand / OrphanQueryWithoutHandler have no handler; registration still succeeds when ValidateOnStartup is off.
	}

	[Fact]
	public async Task OpenGenericHandler_IsClosedOnDemand_SendSucceeds()
	{
		await using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(OpenEchoHandler<>).Assembly);
			cfg.ValidateOnStartup = false;
		});

		var result = await sp.GetRequiredService<ISender>().Send(new EchoCommand<string>("hi"));
		Assert.Equal("hi", result);
	}

	[Fact]
	public async Task OpenGenericQueryHandler_IsClosedOnDemand_SendSucceeds()
	{
		await using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(OpenEchoQueryHandler<>).Assembly);
			cfg.ValidateOnStartup = false;
		});

		var result = await sp.GetRequiredService<ISender>().Send(new EchoQuery<int>(42));
		Assert.Equal(42, result);
	}

	[Fact]
	public async Task ClosedHandler_PreferredOverOpenGeneric()
	{
		await using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(OpenEchoHandler<>).Assembly);
				cfg.ValidateOnStartup = false;
			},
			extra: s =>
			{
				s.AddTransient<ICommandHandler<EchoCommand<string>, string>, ClosedEchoStringHandlerForOverride>();
			});

		var result = await sp.GetRequiredService<ISender>().Send(new EchoCommand<string>("hi"));
		Assert.Equal("closed", result);
	}

	[Fact]
	public void ValidateOnStartup_OpenGenericHandlersInAssembly_DoNotFalseFail()
	{
		var services = new ServiceCollection();
		RegisterAllFixtureHandlers(services);
		services.AddTransient<ICommandHandler<GhostCommand, int>, GhostCommandHandlerForValidation>();

		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(OpenEchoHandler<>).Assembly);
			cfg.ValidateOnStartup = true;
		});

		var registry = services.BuildServiceProvider().GetRequiredService<OpenGenericHandlerRegistry>();
		Assert.True(registry.CanSatisfy(typeof(ICommandHandler<EchoCommand<string>, string>)));
		Assert.True(registry.CanSatisfy(typeof(IQueryHandler<EchoQuery<int>, int>)));
	}

	[Fact]
	public void HandlerLifetime_Singleton_AppliedToDiscoveredHandlers()
	{
		var services = new ServiceCollection();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly);
			cfg.HandlerLifetime = ServiceLifetime.Singleton;
			cfg.ValidateOnStartup = false;
		});

		var descriptor = services.First(d =>
			d.ServiceType == typeof(ICommandHandler<CreateOrder, OrderResult>));
		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
	}

	[Fact]
	public void HandlerLifetime_Scoped_AppliedToDiscoveredHandlers()
	{
		var services = new ServiceCollection();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly);
			cfg.HandlerLifetime = ServiceLifetime.Scoped;
			cfg.ValidateOnStartup = false;
		});

		var descriptor = services.First(d =>
			d.ServiceType == typeof(IQueryHandler<GetOrder, OrderResult>));
		Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
	}

	[Fact]
	public async Task DuplicateHandlers_Send_ThrowsAmbiguous()
	{
		await using var sp = TestHost.Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, AlternateCreateOrderHandler>();
		});

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 1)));

		Assert.Contains("Multiple", ex.Message);
		Assert.Contains(nameof(CreateOrder), ex.Message);
	}

	private static void RegisterAllFixtureHandlers(IServiceCollection services)
	{
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		services.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
		services.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		services.AddTransient<IQueryHandler<TokenProbe, bool>, TokenProbeHandler>();
		services.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
		services.AddTransient<IQueryHandler<ListProducts, Result<PagedResult<string>>>, ListProductsHandler>();
		services.AddTransient<IQueryHandler<ListIds, IReadOnlyList<int>>, ListIdsHandler>();
		services.AddTransient<IQueryHandler<OrphanQueryWithoutHandler, string>, OrphanQueryHandlerForValidation>();
		services.AddTransient<ICommandHandler<FixedPing, string>, FixedPingClosedHandler>();
		services.AddSingleton(new HandlerState());
	}

	private sealed class GhostCommandHandlerForValidation : ICommandHandler<GhostCommand, int>
	{
		public Task<int> Handle(GhostCommand command, CancellationToken cancellationToken)
			=> Task.FromResult(command.Value);
	}

	private sealed class OrphanQueryHandlerForValidation : IQueryHandler<OrphanQueryWithoutHandler, string>
	{
		public Task<string> Handle(OrphanQueryWithoutHandler query, CancellationToken cancellationToken)
			=> Task.FromResult(query.Id.ToString());
	}
}

public sealed record GhostCommand(int Value) : ICommand<int>;

/// <summary>Public message with no handler — used to simulate ValidateOnStartup startup failure.</summary>
public sealed record OrphanQueryWithoutHandler(Guid Id) : IQuery<string>;

public sealed class AlternateCreateOrderHandler : ICommandHandler<CreateOrder, OrderResult>
{
	public Task<OrderResult> Handle(CreateOrder command, CancellationToken cancellationToken)
		=> Task.FromResult(new OrderResult(Guid.NewGuid(), "alt", 0));
}

public sealed record EchoCommand<T>(T Value) : ICommand<T>;

public sealed class OpenEchoHandler<T> : ICommandHandler<EchoCommand<T>, T>
{
	public Task<T> Handle(EchoCommand<T> command, CancellationToken cancellationToken)
		=> Task.FromResult(command.Value);
}

public sealed record EchoQuery<T>(T Value) : IQuery<T>;

public sealed class OpenEchoQueryHandler<T> : IQueryHandler<EchoQuery<T>, T>
{
	public Task<T> Handle(EchoQuery<T> query, CancellationToken cancellationToken)
		=> Task.FromResult(query.Value);
}

/// <summary>Not public-scanned for EchoCommand — registered only in override tests.</summary>
file sealed class ClosedEchoStringHandlerForOverride : ICommandHandler<EchoCommand<string>, string>
{
	public Task<string> Handle(EchoCommand<string> command, CancellationToken cancellationToken)
		=> Task.FromResult("closed");
}

public sealed class FixedPingClosedHandler : ICommandHandler<FixedPing, string>
{
	public Task<string> Handle(FixedPing command, CancellationToken cancellationToken)
		=> Task.FromResult("pong");
}

/// <summary>Closed message satisfied only by an open-generic handler (BBM002 / analyzer).</summary>
public sealed record FixedPing : ICommand<string>;

/// <summary>
/// Open-generic shell around a closed message/response — used by BBM002 tests.
/// Type parameter does not participate in the handler interface (analyzer coverage only).
/// </summary>
public sealed class OpenFixedPingHandler<TDep> : ICommandHandler<FixedPing, string>
{
	public Task<string> Handle(FixedPing command, CancellationToken cancellationToken)
		=> Task.FromResult("pong");
}
