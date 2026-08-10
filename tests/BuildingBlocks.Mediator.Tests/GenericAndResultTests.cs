using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class GenericAndResultTests
{
	[Fact]
	public async Task Command_ResultSuccess_Preserved()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new PlaceOrder("SKU-1"));
		Assert.True(result.IsSuccess);
		Assert.NotEqual(Guid.Empty, result.Value);
	}

	[Fact]
	public async Task Command_ResultFailure_Preserved()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new PlaceOrder(""));
		Assert.False(result.IsSuccess);
		Assert.Equal("sku-required", result.Error);
	}

	[Fact]
	public async Task Behavior_InspectsResult_WithoutSwallowingFailure()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
			s.AddTransient<IPipelineBehavior<PlaceOrder, Result<Guid>>, ResultInspectingBehavior>();
		});

		var result = await sp.GetRequiredService<ISender>().Send(new PlaceOrder(""));
		Assert.False(result.IsSuccess);
		Assert.Contains("result-fail", log);
	}

	[Fact]
	public async Task Query_NestedResultPaged_RoundsTrip()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<ListProducts, Result<PagedResult<string>>>, ListProductsHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new ListProducts(1));
		Assert.True(result.IsSuccess);
		Assert.Equal(2, result.Value!.Items.Count);
	}

	[Fact]
	public async Task Query_IReadOnlyList_ReturnsList()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<ListIds, IReadOnlyList<int>>, ListIdsHandler>());

		var ids = await sp.GetRequiredService<ISender>().Send(new ListIds());
		Assert.Equal(new[] { 1, 2, 3 }, ids);
	}

	[Fact]
	public async Task VoidCommand_AlongsideResultCommand_BothWork()
	{
		var state = new HandlerState();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
		});

		var sender = sp.GetRequiredService<ISender>();
		Assert.True((await sender.Send(new PlaceOrder("A"))).IsSuccess);
		await sender.Send(new CancelOrder(Guid.NewGuid()));
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task Behavior_InspectsResult_SuccessPath()
	{
		var log = new List<string>();
		await using var sp = TestHost.Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>();
			s.AddTransient<IPipelineBehavior<PlaceOrder, Result<Guid>>, ResultInspectingBehavior>();
		});

		var result = await sp.GetRequiredService<ISender>().Send(new PlaceOrder("OK"));
		Assert.True(result.IsSuccess);
		Assert.Contains("result-ok", log);
	}

	[Fact]
	public async Task SendObject_ResultCommand_ReturnsBoxedResult()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<PlaceOrder, Result<Guid>>, PlaceOrderHandler>());

		var boxed = await sp.GetRequiredService<ISender>().Send((object)new PlaceOrder("SKU"));
		var result = Assert.IsType<Result<Guid>>(boxed);
		Assert.True(result.IsSuccess);
	}
}
