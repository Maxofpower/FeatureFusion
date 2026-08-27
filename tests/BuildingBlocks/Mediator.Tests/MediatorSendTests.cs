using Bogus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class MediatorSendTests
{
	[Fact]
	public async Task Send_Command_ReturnsHandlerResult()
	{
		var cmd = new Faker<CreateOrder>()
			.CustomInstantiator(f => new CreateOrder(f.Commerce.ProductName(), f.Random.Int(1, 20)))
			.Generate();

		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());
		var result = await sp.GetRequiredService<ISender>().Send(cmd);

		Assert.Equal(cmd.Product, result.Product);
		Assert.Equal(cmd.Quantity, result.Quantity);
	}

	[Fact]
	public async Task Send_Query_ReturnsHandlerResult()
	{
		var id = Guid.NewGuid();
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new GetOrder(id));
		Assert.Equal(id, result.Id);
	}

	[Fact]
	public async Task Send_VoidCommand_ExecutesHandler()
	{
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid()));
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Send_ViaInterfaceVariable_Command()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		ICommand<OrderResult> cmd = new CreateOrder("X", 1);
		var result = await sp.GetRequiredService<ISender>().Send(cmd);
		Assert.Equal("X", result.Product);
	}

	[Fact]
	public async Task Send_ViaInterfaceVariable_VoidCommand()
	{
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		});

		ICommand cmd = new CancelOrder(Guid.NewGuid());
		await sp.GetRequiredService<ISender>().Send(cmd);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Send_ViaInterfaceVariable_Query()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>());

		IQuery<OrderResult> query = new GetOrder(Guid.NewGuid());
		var result = await sp.GetRequiredService<ISender>().Send(query);
		Assert.Equal("cached", result.Product);
	}

	[Fact]
	public async Task Send_Object_Command_ReturnsBoxedResult()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		object request = new CreateOrder("Box", 2);
		var result = await sp.GetRequiredService<ISender>().Send(request);
		Assert.IsType<OrderResult>(result);
		Assert.Equal("Box", ((OrderResult)result!).Product);
	}

	[Fact]
	public async Task Send_Object_VoidCommand_ReturnsUnit()
	{
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		});

		var result = await sp.GetRequiredService<ISender>().Send((object)new CancelOrder(Guid.NewGuid()));
		Assert.Equal(Unit.Value, result);
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Send_Object_Query_ReturnsBoxedResult()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send((object)new GetOrder(Guid.NewGuid()));
		Assert.IsType<OrderResult>(result);
	}

	[Fact]
	public async Task Send_Object_UnknownType_ThrowsArgumentException()
	{
		await using var sp = TestHost.Build(_ => { });
		await Assert.ThrowsAsync<ArgumentException>(
			() => sp.GetRequiredService<ISender>().Send(new { x = 1 }));
	}

	[Fact]
	public async Task Send_Null_ThrowsArgumentNullException()
	{
		await using var sp = TestHost.Build(_ => { });
		var sender = sp.GetRequiredService<ISender>();
		await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send((ICommand<string>)null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send((object)null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send((IQuery<string>)null!));
	}

	[Fact]
	public async Task Send_MissingHandler_ThrowsInvalidOperationException()
	{
		await using var sp = TestHost.Build(_ => { });
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 1)));
		Assert.Contains(nameof(CreateOrder), ex.Message);
	}

	[Fact]
	public async Task Send_PropagatesCancellationToken()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<TokenProbe, bool>, TokenProbeHandler>());

		Assert.True(await sp.GetRequiredService<ISender>().Send(new TokenProbe(), cts.Token));
	}

	[Fact]
	public async Task Send_ConcurrentCommands_Succeed()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		var faker = new Faker();
		var tasks = Enumerable.Range(0, 40).Select(async _ =>
		{
			using var scope = sp.CreateScope();
			return await scope.ServiceProvider.GetRequiredService<ISender>()
				.Send(new CreateOrder(faker.Commerce.ProductName(), faker.Random.Int(1, 5)));
		});

		var results = await Task.WhenAll(tasks);
		Assert.Equal(40, results.Length);
		Assert.Equal(40, results.Select(r => r.Id).Distinct().Count());
	}

	[Fact]
	public void AddMediator_RegistersISenderAndIMediatorAsSameInstance()
	{
		using var sp = TestHost.BuildWithAddMediator(cfg =>
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly));
		using var scope = sp.CreateScope();
		Assert.Same(
			scope.ServiceProvider.GetRequiredService<ISender>(),
			scope.ServiceProvider.GetRequiredService<IMediator>());
	}

	[Fact]
	public async Task Send_VoidCommand_AsICommandUnit_UsesVoidHandlerPath()
	{
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		});

		ICommand<Unit> asUnit = new CancelOrder(Guid.NewGuid());
		Assert.Equal(Unit.Value, await sp.GetRequiredService<ISender>().Send(asUnit));
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Send_Null_VoidCommand_ThrowsArgumentNullException()
	{
		await using var sp = TestHost.Build(_ => { });
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => sp.GetRequiredService<ISender>().Send((ICommand)null!));
	}

	[Fact]
	public async Task Send_Query_MissingHandler_ThrowsInvalidOperationException()
	{
		await using var sp = TestHost.Build(_ => { });
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new GetOrder(Guid.NewGuid())));
		Assert.Contains(nameof(GetOrder), ex.Message);
	}

	[Fact]
	public async Task Send_Object_Null_ThrowsArgumentNullException()
	{
		await using var sp = TestHost.Build(_ => { });
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => sp.GetRequiredService<ISender>().Send((object)null!));
	}

	[Fact]
	public async Task NestedSend_HandlerInjectsISender_InnerCommandAndVoidRun()
	{
		var state = new NestedSendState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<PlaceAndNotify, NestedSendResult>, PlaceAndNotifyHandler>();
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<ICommandHandler<NotifyOrderCreated>, NotifyOrderCreatedHandler>();
		});

		using var scope = sp.CreateScope();
		var result = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new PlaceAndNotify("nested", 3));

		Assert.Equal("nested", result.Order.Product);
		Assert.Equal(3, result.Order.Quantity);
		Assert.True(result.Notified);
		Assert.True(state.Notified);
		Assert.Equal(result.Order.Id, state.LastNotifiedOrderId);
	}

	[Fact]
	public async Task NestedSend_RunsPipelineAgainForInnerMessage()
	{
		var log = new List<string>();
		var state = new NestedSendState();

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
				s.AddTransient<ICommandHandler<PlaceAndNotify, NestedSendResult>, PlaceAndNotifyHandler>();
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
				s.AddTransient<ICommandHandler<NotifyOrderCreated>, NotifyOrderCreatedHandler>();
			});

		using var scope = sp.CreateScope();
		_ = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new PlaceAndNotify("pipe", 1));

		Assert.Contains("open:PlaceAndNotify", log);
		Assert.Contains("open:CreateOrder", log);
		Assert.Contains("open:NotifyOrderCreated", log);
		// Outer pipeline wraps the outer handler; inner Sends each get their own open:* entry.
		Assert.Equal(3, log.Count(x => x.StartsWith("open:")));
	}
}
