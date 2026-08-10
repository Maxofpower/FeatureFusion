using BuildingBlocks.Mediator.DependencyInjection;
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
}
