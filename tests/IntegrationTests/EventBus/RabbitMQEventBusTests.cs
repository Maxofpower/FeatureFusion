using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.EventBus;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FluentAssertions;
using IntegrationTests.Aspire;
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

	[Fact]
	public async Task Publishes_And_Processes_Failed_Events()
	{
		await _fixture.ResetRabbitMQ();
		var testEvent = new FailingIntegrationEvent(Guid.NewGuid(), 110.0m);
		await PublishAndVerifyDlq(testEvent);
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
	public Task Handle(FailingIntegrationEvent @event)
	{
		throw new InvalidOperationException("Simulated handler failure");
	}
}

public static class Wait
{
	public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (condition()) return;
			await Task.Delay(100);
		}
		throw new TimeoutException("Condition not met within timeout");
	}
}
