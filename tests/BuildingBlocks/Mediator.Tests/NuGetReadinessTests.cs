using BuildingBlocks.Mediator.DependencyInjection;
using BuildingBlocks.Mediator.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class HandlerScannerTests
{
	[Fact]
	public void Scanner_FindsCommandAndQueryHandlers()
	{
		using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>();
		});

		Assert.NotNull(sp.GetService<ICommandHandler<ScannerProbe.Create, string>>());
		Assert.NotNull(sp.GetService<IQueryHandler<ScannerProbe.Get, string>>());
		Assert.NotNull(sp.GetService<ICommandHandler<ScannerProbe.Cancel>>());
	}

	[Fact]
	public void Scanner_SkipsWhenAlreadyRegistered()
	{
		var services = new ServiceCollection();
		services.AddTransient<ICommandHandler<ScannerProbe.Create, string>, ScannerProbe.AlternateCreateHandler>();
		services.AddLogging();
		services.AddMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>());

		var descriptors = services
			.Where(d => d.ServiceType == typeof(ICommandHandler<ScannerProbe.Create, string>))
			.ToList();

		Assert.Single(descriptors);
		Assert.Equal(typeof(ScannerProbe.AlternateCreateHandler), descriptors[0].ImplementationType);
	}

	[Fact]
	public void Scanner_IgnoresAbstractAndOpenGenericHandlers()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>());

		Assert.DoesNotContain(
			services,
			d => d.ImplementationType == typeof(ScannerProbe.AbstractCreateHandler));
		Assert.DoesNotContain(
			services,
			d => d.ImplementationType == typeof(ScannerProbe.OpenGenericHandler<>));
	}

	[Fact]
	public void Scanner_RegistersNestedPublicHandlers()
	{
		using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>();
		});

		Assert.NotNull(sp.GetService<ICommandHandler<ScannerProbe.Nested.NestedCommand, int>>());
	}

	[Fact]
	public void Scanner_RegistersMultiInterfaceHandler()
	{
		using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>();
		});

		Assert.NotNull(sp.GetService<ICommandHandler<ScannerProbe.DualA, string>>());
		Assert.NotNull(sp.GetService<IQueryHandler<ScannerProbe.DualB, string>>());
	}

	[Fact]
	public void RegisterServicesFromAssemblyContaining_Works()
	{
		using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>();
		});

		Assert.NotNull(sp.GetRequiredService<ISender>());
	}
}

public static class ScannerProbe
{
	public sealed record Create(string Name) : ICommand<string>;
	public sealed class CreateHandler : ICommandHandler<Create, string>
	{
		public Task<string> Handle(Create command, CancellationToken cancellationToken)
			=> Task.FromResult(command.Name);
	}

	public sealed class AlternateCreateHandler : ICommandHandler<Create, string>
	{
		public Task<string> Handle(Create command, CancellationToken cancellationToken)
			=> Task.FromResult("alt");
	}

	public sealed record Get(string Id) : IQuery<string>;
	public sealed class GetHandler : IQueryHandler<Get, string>
	{
		public Task<string> Handle(Get query, CancellationToken cancellationToken)
			=> Task.FromResult(query.Id);
	}

	public sealed record Cancel(string Id) : ICommand;
	public sealed class CancelHandler : ICommandHandler<Cancel>
	{
		public Task Handle(Cancel command, CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public abstract class AbstractCreateHandler : ICommandHandler<Create, string>
	{
		public abstract Task<string> Handle(Create command, CancellationToken cancellationToken);
	}

	public sealed class OpenGenericHandler<T> : ICommandHandler<Create, string>
	{
		public Task<string> Handle(Create command, CancellationToken cancellationToken)
			=> Task.FromResult("open");
	}

	public static class Nested
	{
		public sealed record NestedCommand(int Value) : ICommand<int>;
		public sealed class NestedHandler : ICommandHandler<NestedCommand, int>
		{
			public Task<int> Handle(NestedCommand command, CancellationToken cancellationToken)
				=> Task.FromResult(command.Value);
		}
	}

	public sealed record DualA : ICommand<string>;
	public sealed record DualB : IQuery<string>;

	public sealed class DualHandler :
		ICommandHandler<DualA, string>,
		IQueryHandler<DualB, string>
	{
		public Task<string> Handle(DualA command, CancellationToken cancellationToken)
			=> Task.FromResult("a");

		public Task<string> Handle(DualB query, CancellationToken cancellationToken)
			=> Task.FromResult("b");
	}
}

public sealed class BehaviorOrderTests
{
	[Fact]
	public async Task ExplicitOrder_OverridesRegistrationOrder()
	{
		var log = new List<string>();
		await using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.AddOpenBehavior(typeof(NamedOpenBehaviorB<,>), order: 0);
			cfg.AddOpenBehavior(typeof(NamedOpenBehaviorA<,>), order: 100);
		}, s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Equal(new[] { "B:before", "A:before", "handler", "A:after", "B:after" }, log);
	}

	[Fact]
	public async Task UseTelemetry_WithExplicitBehaviorOrder_StillWrapsPipeline()
	{
		var log = new List<string>();
		var activities = new List<System.Diagnostics.Activity>();
		using var listener = new System.Diagnostics.ActivityListener
		{
			ShouldListenTo = s => s.Name == "BuildingBlocks.Mediator.Tests.Order",
			Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
				System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = a => activities.Add(a)
		};
		System.Diagnostics.ActivitySource.AddActivityListener(listener);

		await using var sp = TestHost.BuildWithAddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.UseTelemetry(o =>
			{
				o.ActivitySourceName = "BuildingBlocks.Mediator.Tests.Order";
				o.EnableLogging = false;
			});
			cfg.AddOpenBehavior(typeof(ActivityProbeOpenBehavior<,>), order: 0);
		}, s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Contains("probe:enter:has-activity", log);
		Assert.Contains("handler", log);
		Assert.Single(activities);
	}
}

public sealed class NamedOpenBehaviorA<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;
	public NamedOpenBehaviorA(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("A:before");
		var r = await next(cancellationToken);
		_log.Add("A:after");
		return r;
	}
}

public sealed class NamedOpenBehaviorB<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;
	public NamedOpenBehaviorB(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("B:before");
		var r = await next(cancellationToken);
		_log.Add("B:after");
		return r;
	}
}

public sealed class FilterBehaviorTests
{
	[Fact]
	public async Task CommandPipelineBehavior_SkipsQueries()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ProbeCommandFilter<,>));
		});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("A", 1));
		_ = await sender.Send(new GetOrder(Guid.NewGuid()));

		Assert.Contains("command-filter", log);
		Assert.DoesNotContain("query-filter", log);
	}

	[Fact]
	public async Task QueryPipelineBehavior_SkipsCommands()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ProbeQueryFilter<,>));
		});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("A", 1));
		_ = await sender.Send(new GetOrder(Guid.NewGuid()));

		Assert.Contains("query-filter", log);
		Assert.DoesNotContain("command-filter", log);
	}
}

public sealed class ProbeCommandFilter<TRequest, TResponse> : CommandPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;
	public ProbeCommandFilter(List<string> log) => _log = log;

	protected override async Task<TResponse> HandleCommand(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken)
	{
		_log.Add("command-filter");
		return await next(cancellationToken);
	}
}

public sealed class ProbeQueryFilter<TRequest, TResponse> : QueryPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;
	public ProbeQueryFilter(List<string> log) => _log = log;

	protected override async Task<TResponse> HandleQuery(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken)
	{
		_log.Add("query-filter");
		return await next(cancellationToken);
	}
}

public sealed class CancellationTokenRespectTests
{
	[Fact]
	public async Task Behavior_PassingNone_TokenReachesHandler()
	{
		CancellationToken? seen = null;
		await using var sp = TestHost.Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>>(_ =>
				new TokenCapturingHandler(t => seen = t));
			s.AddTransient(typeof(IPipelineBehavior<,>), typeof(PassNoneTokenBehavior<,>));
		});

		using var cts = new CancellationTokenSource();
		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1), cts.Token);

		Assert.NotNull(seen);
		Assert.False(seen.Value.CanBeCanceled);
		Assert.Equal(CancellationToken.None, seen.Value);
	}
}

public sealed class TokenCapturingHandler : ICommandHandler<CreateOrder, OrderResult>
{
	private readonly Action<CancellationToken> _capture;
	public TokenCapturingHandler(Action<CancellationToken> capture) => _capture = capture;

	public Task<OrderResult> Handle(CreateOrder command, CancellationToken cancellationToken)
	{
		_capture(cancellationToken);
		return Task.FromResult(new OrderResult(Guid.NewGuid(), command.Product, command.Quantity));
	}
}

public sealed class PassNoneTokenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	public Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
		=> next(CancellationToken.None);
}

public sealed class UnsupportedSurfaceTests
{
	[Fact]
	public void Package_HasNoPublishApi()
	{
		Assert.Null(typeof(ISender).GetMethod("Publish"));
		Assert.Null(typeof(IMediator).GetMethod("Publish"));
		Assert.Null(typeof(IMediator).Assembly.GetType("BuildingBlocks.Mediator.INotification"));
	}

	[Fact]
	public void Package_HasNoStreamApi()
	{
		Assert.Null(typeof(ISender).GetMethod("CreateStream"));
		Assert.Null(typeof(IMediator).Assembly.GetType("BuildingBlocks.Mediator.IStreamRequest`1"));
	}

	[Fact]
	public void Package_HasNoNonGenericIQuery()
	{
		Assert.Null(typeof(IMediator).Assembly.GetType("BuildingBlocks.Mediator.IQuery"));
	}

	[Fact]
	public void Package_HasNoFluentValidationOrScrutorOrForeignMediatorDependency()
	{
		var refs = typeof(IMediator).Assembly.GetReferencedAssemblies().Select(a => a.Name);
		Assert.DoesNotContain("FluentValidation", refs);
		Assert.DoesNotContain("Scrutor", refs);
		Assert.DoesNotContain(refs, name =>
			name is not null
			&& name.Contains("Mediat", StringComparison.OrdinalIgnoreCase)
			&& !name.Contains("BuildingBlocks", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void OpenGenericHandler_IsNotAutoClosed()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScannerProbe.Create>());

		Assert.DoesNotContain(
			services,
			d => d.ImplementationType is { IsGenericTypeDefinition: true }
			     && d.ServiceType.IsGenericType
			     && d.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
	}
}
