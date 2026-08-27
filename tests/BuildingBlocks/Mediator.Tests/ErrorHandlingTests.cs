using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class ErrorHandlingTests
{
	[Fact]
	public async Task HandlerException_BubblesWithSameMessage()
	{
		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, ThrowingCreateOrderHandler>());

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 1)));
		Assert.Equal("boom-handler", ex.Message);
	}

	[Fact]
	public async Task MissingHandler_MessageNamesRequestType()
	{
		await using var sp = TestHost.Build(_ => { });
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new GetOrder(Guid.NewGuid())));
		Assert.Contains(nameof(GetOrder), ex.Message);
	}

	[Fact]
	public async Task SendObject_UnknownType_ThrowsArgumentException()
	{
		await using var sp = TestHost.Build(_ => { });
		await Assert.ThrowsAsync<ArgumentException>(
			() => sp.GetRequiredService<ISender>().Send(new object()));
	}

	[Fact]
	public async Task CancelledToken_ReachesHandler()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await using var sp = TestHost.Build(s =>
			s.AddTransient<IQueryHandler<TokenProbe, bool>, TokenProbeHandler>());

		Assert.True(await sp.GetRequiredService<ISender>().Send(new TokenProbe(), cts.Token));
	}

	[Fact]
	public async Task VoidCommand_MissingHandler_ThrowsInvalidOperationException()
	{
		await using var sp = TestHost.Build(_ => { });
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid())));
		Assert.Contains(nameof(CancelOrder), ex.Message);
	}

	[Fact]
	public async Task BehaviorThrowBeforeNext_PreservesExceptionType()
	{
		await using var sp = TestHost.Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IPipelineBehavior<CreateOrder, OrderResult>, ThrowBeforeNextBehavior>();
		});

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 1)));
		Assert.Equal("boom-before", ex.Message);
	}
}
