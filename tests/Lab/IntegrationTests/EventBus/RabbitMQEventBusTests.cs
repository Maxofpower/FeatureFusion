using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.EventBus;
using EventBusRabbitMQ.Utilities;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace IntegrationTests.EventBus;

[Collection(AspireCollection.Name)]
public sealed class RabbitMQEventBusTests : IAsyncLifetime
{
	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;

	public RabbitMQEventBusTests(AspireFixture fixture)
	{
		_fixture = fixture;
		// Share the single WAF host with API tests (avoid a second WithWebHostBuilder host).
		_ = fixture.CreateClient();
		_services = fixture.Services;
	}

	public async Task InitializeAsync() => await _fixture.ResetRabbitMQ();
	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Publishes_And_Processes_Events()
	{
		await TestEventProcessing(
			() => new OrderCreatedIntegrationEvent(Guid.NewGuid(), 99.0m));
	}

	/// <summary>
	/// Smoke: <see cref="FailingIntegrationEvent"/> handler executes once, then permanent failure dead-letters.
	/// See Experiment 11.
	/// </summary>
	[Fact]
	public async Task Publishes_And_Processes_Failed_Events()
	{
		await _fixture.ResetRabbitMQ();
		FailingIntegrationEventHandler.ResetInvocationCount();
		var testEvent = new FailingIntegrationEvent(Guid.NewGuid(), 110.0m);
		var eventBus = GetRequiredService<IEventBus>();

		await eventBus.PublishDirect(testEvent);

		await Wait.UntilAsync(
			() => FailingIntegrationEventHandler.InvocationCount >= 1,
			TimeSpan.FromSeconds(20));

		await using var channel = await CreateChannelAsync();
		var dlqName = GetDlqName();
		await WaitForMessageCount(channel, dlqName, 1, TimeSpan.FromSeconds(20));

		FailingIntegrationEventHandler.InvocationCount.Should().Be(1);
	}

	/// <summary>
	/// Smoke: <see cref="TransientThrowingIntegrationEvent"/> is retried until RetryCount handler
	/// executions, then dead-letters. See Experiment 11.
	/// </summary>
	[Fact]
	public async Task Transient_handler_failure_is_retried_on_redelivery()
	{
		await _fixture.ResetRabbitMQ();
		TransientThrowingIntegrationEventHandler.ResetInvocationCount();
		var retryCount = GetRequiredService<IOptions<EventBusOptions>>().Value.RetryCount;
		var testEvent = new TransientThrowingIntegrationEvent(Guid.NewGuid(), 120.0m);
		var eventBus = GetRequiredService<IEventBus>();

		await eventBus.PublishDirect(testEvent);

		await Wait.UntilAsync(
			() => TransientThrowingIntegrationEventHandler.InvocationCount >= retryCount,
			TimeSpan.FromSeconds(30));

		await using var channel = await CreateChannelAsync();
		var dlqName = GetDlqName();
		await WaitForMessageCount(channel, dlqName, 1, TimeSpan.FromSeconds(20));

		TransientThrowingIntegrationEventHandler.InvocationCount.Should().Be(retryCount);
	}

	/// <summary>
	/// Smoke: first <see cref="TransientException"/> requeues; the next handler execution succeeds.
	/// </summary>
	[Fact]
	public async Task Transient_handler_failure_can_succeed_on_retry()
	{
		await _fixture.ResetRabbitMQ();
		OnceTransientThenSucceedIntegrationEventHandler.Reset();
		var testEvent = new OnceTransientThenSucceedIntegrationEvent(Guid.NewGuid(), 140.0m);
		var eventBus = GetRequiredService<IEventBus>();

		await eventBus.PublishDirect(testEvent);

		await Wait.UntilAsync(
			() => OnceTransientThenSucceedIntegrationEventHandler.InvocationCountFor(testEvent.Id) >= 2,
			TimeSpan.FromSeconds(30));

		await Task.Delay(1000);

		OnceTransientThenSucceedIntegrationEventHandler.InvocationCountFor(testEvent.Id).Should().Be(2);
	}

	[Fact]
	public async Task Verify_Message_Flow()
	{
		await _fixture.ResetRabbitMQ();
		var testEvent = new OrderCreatedIntegrationEvent(Guid.NewGuid(), 99.0m);
		await VerifyMessageFlow(testEvent, "OrderCreatedIntegrationEvent");
	}

	[Fact]
	public async Task Processes_Published_Event_Once()
	{
		_fixture.ProcessedEvents.Clear();
		var eventBus = GetRequiredService<IEventBus>();
		var testEvent = new OrderCreatedIntegrationEvent(Guid.NewGuid(), 100.0m);

		await eventBus.PublishDirect(testEvent);
		await Wait.UntilAsync(() => _fixture.ProcessedEvents.Any(e => e.Id == testEvent.Id), TimeSpan.FromSeconds(20));

		_fixture.ProcessedEvents.Should().ContainSingle(e => e.Id == testEvent.Id);
	}

	[Fact]
	public async Task Should_Not_Requeue_Invalid_Messages()
	{
		await using var channel = await CreateChannelAsync();
		var dlqName = GetDlqName();

		await channel.QueuePurgeAsync(dlqName);

		var props = new BasicProperties
		{
			MessageId = Guid.NewGuid().ToString(),
			Headers = new Dictionary<string, object?>
			{
				[RabbitMQConstants.EventTypeHeader] = "NonExistentEventType",
				[RabbitMQConstants.SourceServiceHeader] = "TestService"
			}
		};

		await channel.BasicPublishAsync(
			exchange: RabbitMQConstants.MainExchangeName,
			routingKey: "OrderCreatedIntegrationEvent",
			mandatory: true,
			basicProperties: props,
			body: Encoding.UTF8.GetBytes("{ invalid json }"));

		await WaitForMessageCount(channel, dlqName, 1);
		var dlqMessage = await channel.BasicGetAsync(dlqName, autoAck: true);

		dlqMessage.Should().NotBeNull();
		dlqMessage!.BasicProperties.MessageId.Should().Be(props.MessageId);
	}

	private async Task TestEventProcessing<T>(Func<T> eventFactory) where T : IntegrationEvent
	{
		_fixture.ProcessedEvents.Clear();
		var testEvent = eventFactory();
		var eventBus = GetRequiredService<IEventBus>();

		await eventBus.PublishDirect(testEvent);
		await Wait.UntilAsync(() => _fixture.ProcessedEvents.Any(), TimeSpan.FromSeconds(20));

		_fixture.ProcessedEvents.Should().ContainSingle(e => e.Id == testEvent.Id);
	}

	private async Task VerifyMessageFlow(OrderCreatedIntegrationEvent testEvent, string routingKey)
	{
		_fixture.ProcessedEvents.Clear();
		await using var channel = await CreateChannelAsync();
		var testQueue = "test_feature_fusion";

		await channel.QueueDeclareAsync(testQueue, durable: true, exclusive: false, autoDelete: false);
		await channel.QueueBindAsync(testQueue, RabbitMQConstants.MainExchangeName, routingKey);

		await GetRequiredService<IEventBus>().PublishDirect(testEvent);
		await WaitForMessageCount(channel, testQueue, 1);

		var message = await channel.BasicGetAsync(testQueue, autoAck: false);
		message.Should().NotBeNull();
		message!.BasicProperties.MessageId.Should().Be(testEvent.Id.ToString());

		await channel.QueueDeleteAsync(testQueue);
	}

	private async Task PublishAndVerifyDlq(IntegrationEvent testEvent)
	{
		await using var channel = await CreateChannelAsync();
		var dlqName = GetDlqName();
		await channel.QueuePurgeAsync(dlqName);
		await GetRequiredService<IEventBus>().PublishDirect(testEvent);

		var foundMessage = await WaitForMessageByIdAsync(
			channel: channel,
			queueName: dlqName,
			testEvent.Id,
			acknowledgeIfFound: true,
			timeout: TimeSpan.FromSeconds(60));

		foundMessage.Should().NotBeNull();
		var deserialized = JsonSerializer.Deserialize(
			Encoding.UTF8.GetString(foundMessage!.Body.Span),
			testEvent.GetType());

		deserialized.Should().BeEquivalentTo(testEvent);
	}

	private async Task<IChannel> CreateChannelAsync() =>
		await GetRequiredService<IRabbitMQPersistentConnection>().CreateChannelAsync();

	private string GetDlqName() =>
		$"{GetRequiredService<IOptions<EventBusOptions>>().Value.SubscriptionClientName}_dlq";

	private T GetRequiredService<T>() where T : notnull =>
		_services.GetRequiredService<T>();

	private async Task WaitForMessageCount(
		IChannel channel,
		string queueName,
		int expectedCount,
		TimeSpan? timeout = null)
	{
		var timeoutValue = timeout ?? TimeSpan.FromSeconds(30);
		var sw = Stopwatch.StartNew();

		while (channel.IsOpen && sw.Elapsed < timeoutValue)
		{
			var queue = await channel.QueueDeclarePassiveAsync(queueName);
			if (queue.MessageCount >= expectedCount)
				return;

			await Task.Delay(200);
		}

		throw new TimeoutException($"Queue '{queueName}' didn't reach {expectedCount} messages");
	}

	private static async Task<BasicGetResult?> WaitForMessageByIdAsync(
		IChannel channel,
		string queueName,
		Guid expectedMessageId,
		bool acknowledgeIfFound,
		TimeSpan timeout)
	{
		var startTime = DateTime.UtcNow;

		while (DateTime.UtcNow - startTime < timeout)
		{
			var message = await channel.BasicGetAsync(queueName, autoAck: false);
			if (message != null)
			{
				if (Guid.Parse(message.BasicProperties.MessageId!) == expectedMessageId)
				{
					if (acknowledgeIfFound)
						await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
					return message;
				}
				await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true);
			}
			await Task.Delay(200);
		}
		return null;
	}
}

public record TestIntegrationEvent : IntegrationEvent;

public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
	public List<TestIntegrationEvent> ReceivedEvents { get; } = new();

	public Task Handle(TestIntegrationEvent @event)
	{
		ReceivedEvents.Add(@event);
		return Task.CompletedTask;
	}
}

public record FailingIntegrationEvent : IntegrationEvent
{
	public decimal Total { get; }

	public FailingIntegrationEvent(Guid id, decimal total)
	{
		Id = id;
		Total = total;
	}
}

public class FailingIntegrationEventHandler : IIntegrationEventHandler<FailingIntegrationEvent>
{
	private static int _invocationCount;

	/// <summary>Test-only observation counter for behavioral experiments.</summary>
	public static int InvocationCount => _invocationCount;

	public static void ResetInvocationCount() =>
		Interlocked.Exchange(ref _invocationCount, 0);

	public Task Handle(FailingIntegrationEvent @event)
	{
		Interlocked.Increment(ref _invocationCount);
		throw new InvalidOperationException("Simulated handler failure");
	}
}

public record TransientThrowingIntegrationEvent : IntegrationEvent
{
	public decimal Total { get; }

	public TransientThrowingIntegrationEvent(Guid id, decimal total)
	{
		Id = id;
		Total = total;
	}
}

public sealed class TransientThrowingIntegrationEventHandler
	: IIntegrationEventHandler<TransientThrowingIntegrationEvent>
{
	private static int _invocationCount;

	public static int InvocationCount => _invocationCount;

	public static void ResetInvocationCount() =>
		Interlocked.Exchange(ref _invocationCount, 0);

	public Task Handle(TransientThrowingIntegrationEvent @event)
	{
		Interlocked.Increment(ref _invocationCount);
		throw new TransientException("Simulated transient handler failure");
	}
}

public record BusinessFailureIntegrationEvent : IntegrationEvent
{
	public decimal Total { get; }

	public BusinessFailureIntegrationEvent(Guid id, decimal total)
	{
		Id = id;
		Total = total;
	}
}

public sealed class BusinessFailureIntegrationEventHandler
	: IIntegrationEventHandler<BusinessFailureIntegrationEvent>
{
	private static int _invocationCount;

	public static int InvocationCount => _invocationCount;

	public static void ResetInvocationCount() =>
		Interlocked.Exchange(ref _invocationCount, 0);

	public Task Handle(BusinessFailureIntegrationEvent @event)
	{
		Interlocked.Increment(ref _invocationCount);
		throw new BusinessException("Simulated business handler failure");
	}
}

public record OnceTransientThenSucceedIntegrationEvent : IntegrationEvent
{
	public decimal Total { get; }

	public OnceTransientThenSucceedIntegrationEvent(Guid id, decimal total)
	{
		Id = id;
		Total = total;
	}
}

public sealed class OnceTransientThenSucceedIntegrationEventHandler
	: IIntegrationEventHandler<OnceTransientThenSucceedIntegrationEvent>
{
	private static readonly ConcurrentDictionary<Guid, int> AttemptsByMessageId = new();

	public static int InvocationCountFor(Guid messageId) =>
		AttemptsByMessageId.TryGetValue(messageId, out var count) ? count : 0;

	public static void Reset() => AttemptsByMessageId.Clear();

	public Task Handle(OnceTransientThenSucceedIntegrationEvent @event)
	{
		var attempt = AttemptsByMessageId.AddOrUpdate(@event.Id, 1, (_, previous) => previous + 1);
		if (attempt == 1)
		{
			throw new TransientException("Simulated first-attempt transient failure");
		}

		return Task.CompletedTask;
	}
}

