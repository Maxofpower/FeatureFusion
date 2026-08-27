using BuildingBlocks.Mediator.DependencyInjection;
using BuildingBlocks.Mediator.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class PipelineBehaviorTests
{
	[Fact]
	public async Task Pipeline_SingleBehavior_WrapsHandler()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, RecordingBehavior>();
		});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Equal(new[] { "before", "handler", "after" }, log);
	}

	[Fact]
	public async Task Pipeline_FirstRegistered_IsOutermost()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>>(_ => new OrderedBehavior("A", log));
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>>(_ => new OrderedBehavior("B", log));
		});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Equal(new[] { "A:before", "B:before", "handler", "B:after", "A:after" }, log);
	}

	[Fact]
	public async Task Pipeline_ShortCircuit_SkipsHandler()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, ShortCircuitBehavior>();
		});

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Equal("short", result.Product);
		Assert.DoesNotContain("handler", log);
		Assert.Contains("short-circuit", log);
	}

	[Fact]
	public async Task Pipeline_ThrowBeforeNext_HandlerNeverRuns()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, ThrowBeforeNextBehavior>();
		});

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1)));
		Assert.Equal("boom-before", ex.Message);
		Assert.DoesNotContain("handler", log);
	}

	[Fact]
	public async Task Pipeline_ThrowAfterNext_HandlerRan()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, ThrowAfterNextBehavior>();
		});

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1)));
		Assert.Equal("boom-after", ex.Message);
		Assert.Contains("handler", log);
		Assert.Contains("after-next", log);
	}

	[Fact]
	public async Task Pipeline_TwoOpenBehaviors_BothRun()
	{
		var log = new List<string>();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(OpenLoggingBehavior<,>));
				cfg.AddOpenBehavior(typeof(OpenSecondBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("X", 1));
		Assert.Contains("open:CreateOrder", log);
		Assert.Contains("open2:CreateOrder", log);
	}

	[Fact]
	public async Task VoidCommand_Pipeline_BindsToConcreteCommandType()
	{
		var log = new List<string>();
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			s.AddTransient<IPipelineBehavior<CancelOrder, Unit>, VoidCommandBehavior>();
		});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid()));
		Assert.Equal(new[] { "void-before", "void-handler", "void-after" }, log);
	}

	[Fact]
	public async Task Pipeline_CommandOnlyFilter_SkipsQueries()
	{
		var log = new List<string>();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(CommandOnlyBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("X", 1));
		_ = await sender.Send(new GetOrder(Guid.NewGuid()));

		Assert.Contains("cmd-only:CreateOrder", log);
		Assert.DoesNotContain(log, x => x.StartsWith("cmd-only:GetOrder"));
	}

	[Fact]
	public async Task Pipeline_QueryOnlyFilter_SkipsCommands()
	{
		var log = new List<string>();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(QueryOnlyBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("X", 1));
		_ = await sender.Send(new GetOrder(Guid.NewGuid()));

		Assert.Contains("query-only:GetOrder", log);
		Assert.DoesNotContain(log, x => x.StartsWith("query-only:CreateOrder"));
	}

	[Fact]
	public async Task Pipeline_VoidAndResponse_IsolatedInSameProvider()
	{
		var log = new List<string>();
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, RecordingBehavior>();
			s.AddTransient<IPipelineBehavior<CancelOrder, Unit>, VoidCommandBehavior>();
		});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("A", 1));
		await sender.Send(new CancelOrder(Guid.NewGuid()));

		Assert.Contains("before", log);
		Assert.Contains("void-before", log);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public void AddOpenBehavior_NonOpenGeneric_Throws()
	{
		var cfg = new MediatorConfiguration();
		Assert.Throws<ArgumentException>(() => cfg.AddOpenBehavior(typeof(RecordingBehavior)));
	}

	[Fact]
	public void AddOpenBehavior_NonPipelineType_Throws()
	{
		var cfg = new MediatorConfiguration();
		Assert.Throws<ArgumentException>(() => cfg.AddOpenBehavior(typeof(List<>)));
	}

	[Fact]
	public async Task Pipeline_NoBehaviors_HandlerRunsAlone()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("solo", 3));
		Assert.Equal("solo", result.Product);
		Assert.Equal(3, result.Quantity);
	}

	[Fact]
	public async Task Pipeline_OpenBehavior_RunsOnVoidCommand()
	{
		var log = new List<string>();
		var state = new HandlerState();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(OpenLoggingBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddSingleton(state);
				s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid()));
		Assert.Contains("open:CancelOrder", log);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task ConstrainedCommandBehavior_IsNotConstructedForQueries()
	{
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(ConstrainedCommandBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counter);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new GetOrder(Guid.NewGuid()));
		Assert.Equal(0, counter.CommandConstructions);
		Assert.DoesNotContain(counter.Log, x => x.StartsWith("constrained-cmd:"));

		_ = await sender.Send(new CreateOrder("X", 1));
		Assert.True(counter.CommandConstructions >= 1);
		Assert.Contains("constrained-cmd:CreateOrder", counter.Log);
	}

	[Fact]
	public async Task ConstrainedQueryBehavior_IsNotConstructedForCommands()
	{
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenQueryBehavior(typeof(ConstrainedQueryBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counter);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();
		_ = await sender.Send(new CreateOrder("X", 1));
		Assert.Equal(0, counter.QueryConstructions);
		Assert.DoesNotContain(counter.Log, x => x.StartsWith("constrained-query:"));

		_ = await sender.Send(new GetOrder(Guid.NewGuid()));
		Assert.True(counter.QueryConstructions >= 1);
		Assert.Contains("constrained-query:GetOrder", counter.Log);
	}

	[Fact]
	public async Task ConstrainedCommandBehavior_RunsOnVoidCommand()
	{
		var counter = new ConstrainedBehaviorCounter();
		var state = new HandlerState();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenCommandBehavior(typeof(ConstrainedCommandBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counter);
				s.AddSingleton(state);
				s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid()));
		Assert.True(counter.CommandConstructions >= 1);
		Assert.Contains("constrained-cmd:CancelOrder", counter.Log);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task ConstrainedAndUnconstrainedBehaviors_SharePipelineOrder()
	{
		var log = new List<string>();
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(OpenLoggingBehavior<,>), order: 0);
				cfg.AddOpenCommandBehavior(typeof(ConstrainedCommandBehavior<,>), order: 10);
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddSingleton(counter);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("X", 1));
		Assert.Contains("open:CreateOrder", log);
		Assert.Contains("constrained-cmd:CreateOrder", counter.Log);
	}

	[Fact]
	public async Task ConstrainedCommandAndQueryBehaviors_IsolateByKind()
	{
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenCommandBehavior(typeof(ConstrainedCommandBehavior<,>));
				cfg.AddOpenQueryBehavior(typeof(ConstrainedQueryBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counter);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();

		_ = await sender.Send(new CreateOrder("X", 1));
		Assert.True(counter.CommandConstructions >= 1);
		Assert.Equal(0, counter.QueryConstructions);
		Assert.Contains("constrained-cmd:CreateOrder", counter.Log);
		Assert.DoesNotContain(counter.Log, x => x.StartsWith("constrained-query:"));

		_ = await sender.Send(new GetOrder(Guid.NewGuid()));
		Assert.True(counter.QueryConstructions >= 1);
		Assert.Contains("constrained-query:GetOrder", counter.Log);
		Assert.DoesNotContain(counter.Log, x => x == "constrained-cmd:GetOrder");
	}

	[Fact]
	public async Task ConstrainedBehaviors_RunOnOpenGenericMessages()
	{
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(OpenEchoHandler<>).Assembly);
				cfg.AddOpenCommandBehavior(typeof(ConstrainedCommandBehavior<,>));
				cfg.AddOpenQueryBehavior(typeof(ConstrainedQueryBehavior<,>));
			},
			s => s.AddSingleton(counter));

		var sender = sp.GetRequiredService<ISender>();

		Assert.Equal("hi", await sender.Send(new EchoCommand<string>("hi")));
		Assert.True(counter.CommandConstructions >= 1);
		Assert.Contains($"constrained-cmd:{typeof(EchoCommand<string>).Name}", counter.Log);

		Assert.Equal(42, await sender.Send(new EchoQuery<int>(42)));
		Assert.True(counter.QueryConstructions >= 1);
		Assert.Contains($"constrained-query:{typeof(EchoQuery<int>).Name}", counter.Log);
	}

	[Fact]
	public async Task ConstrainedBehaviors_RunOnResultAndNestedGenericResponse()
	{
		var counter = new ConstrainedBehaviorCounter();
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenCommandBehavior(typeof(ConstrainedCommandBehavior<,>));
				cfg.AddOpenQueryBehavior(typeof(ConstrainedQueryBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counter);
				s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
				s.AddTransient<IQueryHandler<ListProducts, Result<PagedResult<string>>>, ListProductsHandler>();
				s.AddTransient<IQueryHandler<ListIds, IReadOnlyList<int>>, ListIdsHandler>();
			});

		var sender = sp.GetRequiredService<ISender>();

		var placed = await sender.Send(new PlaceOrder("SKU-1"));
		Assert.True(placed.IsSuccess);
		Assert.Contains("constrained-cmd:PlaceOrder", counter.Log);

		var products = await sender.Send(new ListProducts(1));
		Assert.True(products.IsSuccess);
		Assert.Equal(2, products.Value!.Items.Count);
		Assert.Contains("constrained-query:ListProducts", counter.Log);

		Assert.Equal(new[] { 1, 2, 3 }, await sender.Send(new ListIds()));
		Assert.Contains("constrained-query:ListIds", counter.Log);
	}

	[Fact]
	public void AddOpenCommandBehavior_UnconstrainedPipelineType_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenCommandBehavior(typeof(OpenLoggingBehavior<,>)),
			typeof(OpenLoggingBehavior<,>),
			"ICommandPipelineBehavior");
	}

	[Fact]
	public void AddOpenQueryBehavior_UnconstrainedPipelineType_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenQueryBehavior(typeof(OpenLoggingBehavior<,>)),
			typeof(OpenLoggingBehavior<,>),
			"IQueryPipelineBehavior");
	}

	[Fact]
	public void AddOpenCommandBehavior_WrongKind_QueryBehavior_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenCommandBehavior(typeof(ConstrainedQueryBehavior<,>)),
			typeof(ConstrainedQueryBehavior<,>),
			"ICommandPipelineBehavior",
			mustNotContain: "IQueryPipelineBehavior");
	}

	[Fact]
	public void AddOpenQueryBehavior_WrongKind_CommandBehavior_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenQueryBehavior(typeof(ConstrainedCommandBehavior<,>)),
			typeof(ConstrainedCommandBehavior<,>),
			"IQueryPipelineBehavior",
			mustNotContain: "ICommandPipelineBehavior");
	}

	[Fact]
	public void AddOpenCommandBehavior_LegacyRuntimeSkipBase_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenCommandBehavior(typeof(CommandPipelineBehavior<,>)),
			typeof(CommandPipelineBehavior<,>),
			"ICommandPipelineBehavior");
	}

	[Fact]
	public void AddOpenQueryBehavior_LegacyRuntimeSkipBase_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenQueryBehavior(typeof(QueryPipelineBehavior<,>)),
			typeof(QueryPipelineBehavior<,>),
			"IQueryPipelineBehavior");
	}

	[Fact]
	public void AddOpenCommandBehavior_NonPipelineOpenGeneric_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenCommandBehavior(typeof(List<>)),
			typeof(List<>),
			"ICommandPipelineBehavior");
	}

	[Fact]
	public void AddOpenQueryBehavior_NonPipelineOpenGeneric_Throws()
	{
		AssertRejectedOpenKind(
			() => new MediatorConfiguration().AddOpenQueryBehavior(typeof(List<>)),
			typeof(List<>),
			"IQueryPipelineBehavior");
	}

	[Fact]
	public void AddOpenCommandBehavior_NonOpenGeneric_Throws()
	{
		var ex = Assert.Throws<ArgumentException>(
			() => new MediatorConfiguration().AddOpenCommandBehavior(typeof(RecordingBehavior)));
		Assert.Equal("openBehaviorType", ex.ParamName);
		Assert.Contains(nameof(RecordingBehavior), ex.Message, StringComparison.Ordinal);
		Assert.Contains("open generic type definition", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddOpenQueryBehavior_NonOpenGeneric_Throws()
	{
		var ex = Assert.Throws<ArgumentException>(
			() => new MediatorConfiguration().AddOpenQueryBehavior(typeof(RecordingBehavior)));
		Assert.Equal("openBehaviorType", ex.ParamName);
		Assert.Contains(nameof(RecordingBehavior), ex.Message, StringComparison.Ordinal);
		Assert.Contains("open generic type definition", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddOpenCommandBehavior_Null_Throws()
	{
		var ex = Assert.Throws<ArgumentNullException>(
			() => new MediatorConfiguration().AddOpenCommandBehavior(null!));
		Assert.Equal("openBehaviorType", ex.ParamName);
	}

	[Fact]
	public void AddOpenQueryBehavior_Null_Throws()
	{
		var ex = Assert.Throws<ArgumentNullException>(
			() => new MediatorConfiguration().AddOpenQueryBehavior(null!));
		Assert.Equal("openBehaviorType", ex.ParamName);
	}

	private static void AssertRejectedOpenKind(
		Action register,
		Type wrongType,
		string requiredInterface,
		string? mustNotContain = null)
	{
		var ex = Assert.Throws<ArgumentException>(register);
		Assert.Equal("openBehaviorType", ex.ParamName);
		Assert.Contains(wrongType.Name, ex.Message, StringComparison.Ordinal);
		Assert.Contains($"must implement {requiredInterface}", ex.Message, StringComparison.Ordinal);
		if (mustNotContain is not null)
			Assert.DoesNotContain(mustNotContain, ex.Message, StringComparison.Ordinal);
	}
}
