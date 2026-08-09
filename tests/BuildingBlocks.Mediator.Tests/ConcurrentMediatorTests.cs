using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

/// <summary>
/// Concurrent Send smoke tests: each parallel call uses its own DI scope (scoped ISender).
/// Pipeline must still wrap every call without lost enter/exit or broken per-request order.
/// </summary>
public sealed class ConcurrentMediatorTests
{
	private const int Parallelism = 64;

	[Fact]
	public async Task Concurrent_Send_WithOpenPipeline_AllEnterAndExit()
	{
		var counters = new ConcurrentPipelineCounters();

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(ConcurrentCountingBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counters);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		var tasks = Enumerable.Range(0, Parallelism).Select(async i =>
		{
			using var scope = sp.CreateScope();
			return await scope.ServiceProvider.GetRequiredService<ISender>()
				.Send(new CreateOrder($"p-{i}", i + 1));
		});

		var results = await Task.WhenAll(tasks);

		Assert.Equal(Parallelism, results.Length);
		Assert.Equal(Parallelism, counters.Entered);
		Assert.Equal(Parallelism, counters.Exited);
		Assert.All(results, r => Assert.StartsWith("p-", r.Product));
	}

	[Fact]
	public async Task Concurrent_Send_PipelineOrder_PreservedPerRequest()
	{
		var log = new ConcurrentOrderLog();

		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<ConcurrentCreateOrder, OrderResult>, ConcurrentCreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<ConcurrentCreateOrder, OrderResult>>(
				_ => new ConcurrentOrderedBehavior("A", log));
			s.AddTransient<IPipelineBehavior<ConcurrentCreateOrder, OrderResult>>(
				_ => new ConcurrentOrderedBehavior("B", log));
		});

		var ids = Enumerable.Range(0, Parallelism).Select(_ => Guid.NewGuid()).ToArray();

		var tasks = ids.Select(async id =>
		{
			using var scope = sp.CreateScope();
			return await scope.ServiceProvider.GetRequiredService<ISender>()
				.Send(new ConcurrentCreateOrder(id, "x", 1));
		});

		var results = await Task.WhenAll(tasks);
		Assert.Equal(Parallelism, results.Length);

		foreach (var id in ids)
		{
			Assert.Equal(
				new[] { "A:before", "B:before", "handler", "B:after", "A:after" },
				log.GetSteps(id));
		}
	}

	[Fact]
	public async Task Concurrent_MixedCommandQueryVoid_WithOpenPipeline_Succeeds()
	{
		var counters = new ConcurrentPipelineCounters();
		var state = new HandlerState();

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(ConcurrentCountingBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counters);
				s.AddSingleton(state);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
				s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			});

		var tasks = Enumerable.Range(0, Parallelism).Select(async i =>
		{
			using var scope = sp.CreateScope();
			var sender = scope.ServiceProvider.GetRequiredService<ISender>();

			return (i % 3) switch
			{
				0 => (object)await sender.Send(new CreateOrder($"c-{i}", 1)),
				1 => (object)await sender.Send(new GetOrder(Guid.NewGuid())),
				_ => await SendVoid(sender, Guid.NewGuid())
			};
		});

		var results = await Task.WhenAll(tasks);

		Assert.Equal(Parallelism, results.Length);
		Assert.Equal(Parallelism, counters.Entered);
		Assert.Equal(Parallelism, counters.Exited);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Concurrent_SendObject_WithPipeline_Succeeds()
	{
		var counters = new ConcurrentPipelineCounters();

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(ConcurrentCountingBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counters);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		var tasks = Enumerable.Range(0, Parallelism).Select(async i =>
		{
			using var scope = sp.CreateScope();
			return await scope.ServiceProvider.GetRequiredService<ISender>()
				.Send((object)new CreateOrder($"obj-{i}", 1));
		});

		var results = await Task.WhenAll(tasks);

		Assert.Equal(Parallelism, results.Length);
		Assert.All(results, r => Assert.IsType<OrderResult>(r));
		Assert.Equal(Parallelism, counters.Entered);
		Assert.Equal(Parallelism, counters.Exited);
	}

	[Fact]
	public async Task Concurrent_SameScope_SequentialSends_PipelineStable()
	{
		// Same scoped ISender reused on one thread (not cross-thread): pipeline still wraps each send.
		var counters = new ConcurrentPipelineCounters();

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.AddOpenBehavior(typeof(ConcurrentCountingBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(counters);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		using (var scope = sp.CreateScope())
		{
			var sender = scope.ServiceProvider.GetRequiredService<ISender>();
			for (var i = 0; i < 20; i++)
				_ = await sender.Send(new CreateOrder($"seq-{i}", 1));
		}

		Assert.Equal(20, counters.Entered);
		Assert.Equal(20, counters.Exited);
	}

	private static async Task<object> SendVoid(ISender sender, Guid id)
	{
		await sender.Send(new CancelOrder(id));
		return Unit.Value;
	}
}
