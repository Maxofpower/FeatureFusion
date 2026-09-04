using System.Diagnostics;
using System.Text.Json;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.AsyncTraceCorrelation;

/// <summary>
/// Experiment 18 — async trace correlation across HTTP → outbox → RabbitMQ → consumer.
/// Characterizes current production telemetry: whether the originating HTTP/Mediator TraceId
/// is preserved into <c>EventBus</c> <c>ProcessMessage</c>, or whether the consumer starts a
/// separate trace and only business ids (<c>IntegrationEvent.Id</c> / <c>OrderId</c>) remain
/// correlatable. Does not modify production EventBus/Telemetry. Does not inject RabbitMQ
/// <c>traceparent</c>. HTTP <c>traceparent</c> uses the same Lab client convention as Exp 8
/// so the originating operation TraceId is known. OutBoxWorker and PublishDirect currently
/// emit no Activities; ProcessMessage starts a root Activity with message.id tags only.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class AsyncTraceCorrelationExperimentTests
{
	private const int Quantity = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public AsyncTraceCorrelationExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_services = fixture.Services;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Http_order_outbox_consumer_trace_correlation_is_characterized()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var key = System.Ulid.NewUlid().ToString();

		var (originTraceId, originSpanId) = NewTraceParent();
		var originTraceHex = originTraceId.ToHexString();

		var httpResult = await HttpOrderCreate.PostAsync(
			_http,
			capture,
			key,
			Quantity,
			traceId: originTraceId,
			parentSpanId: originSpanId,
			jsonOptions: JsonOptions);

		httpResult.HttpStatus.Should().Be(200, httpResult.Body);
		httpResult.OrderId.Should().NotBeEmpty();

		var originSpans = capture.ForTrace(originTraceId);
		var originHasAspNet = originSpans.Any(IsAspNetCore);
		var originHasMediator = originSpans.Any(IsMediator);
		var originHasNpgsql = originSpans.Any(IsNpgsql);
		var originEventBusSpans = originSpans.Where(IsEventBusProcessMessage).ToList();

		var outboxRow = await OrderOutboxObserver.WaitUntilProcessedAsync(_services, httpResult.OrderId);
		var outbox = ToSnapshot(outboxRow);
		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == httpResult.OrderId),
			TimeSpan.FromSeconds(20));

		var delivered = _fixture.ProcessedEvents.Single(e => e.OrderId == httpResult.OrderId);
		delivered.Id.Should().Be(outbox.IntegrationEventId,
			"handler IntegrationEvent.Id must match outbox_messages.Id");

		await WaitForInboxProcessedAsync(delivered.Id);
		var inbox = await QueryInboxAsync(delivered.Id);

		await Wait.UntilAsync(
			() => capture.All.Any(s => IsEventBusProcessMessage(s) && HasMessageId(s, delivered.Id)),
			TimeSpan.FromSeconds(10));

		var processSpansForMessage = capture.All
			.Where(s => IsEventBusProcessMessage(s) && HasMessageId(s, delivered.Id))
			.ToList();

		var consumerTraceIds = processSpansForMessage.Select(s => s.TraceId).Distinct().ToList();
		var consumerSharesOriginTrace = consumerTraceIds.Contains(originTraceHex, StringComparer.Ordinal);
		var consumerHasParentOnOrigin = processSpansForMessage.Any(s =>
			s.ParentSpanId is not null
			&& originSpans.Any(o => o.SpanId == s.ParentSpanId));
		var consumerLinksToOrigin = processSpansForMessage.Any(s =>
			s.Links.Any(l => string.Equals(l.TraceId, originTraceHex, StringComparison.Ordinal)));
		var outboxWorkerSpans = capture.All.Where(s =>
			s.DisplayName.Contains("Outbox", StringComparison.OrdinalIgnoreCase)
			|| s.Source.Contains("Outbox", StringComparison.OrdinalIgnoreCase)).ToList();
		var rabbitPublishSpans = capture.All.Where(s =>
			s.DisplayName.Contains("publish", StringComparison.OrdinalIgnoreCase)
			&& (s.Source.Contains("Rabbit", StringComparison.OrdinalIgnoreCase)
				|| s.Source.Contains("EventBus", StringComparison.OrdinalIgnoreCase))).ToList();

		var w3cCrossesRabbitBoundary = consumerSharesOriginTrace
			|| consumerHasParentOnOrigin
			|| consumerLinksToOrigin;

		var characterization = w3cCrossesRabbitBoundary
			? "W3C_TRACE_CONTEXT_CORRELATED"
			: "SEPARATE_CONSUMER_TRACE_BUSINESS_ID_CORRELATION_ONLY";

		var result = new
		{
			name = "http-order-async-trace-correlation-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				path = HttpOrderCreate.Path,
				asyncPath = "POST order â†’ CreateOrderCommandHandler â†’ IntegrationEventService â†’ outbox_messages â†’ OutBoxWorker â†’ PublishDirect â†’ RabbitMQ â†’ EventBus.ProcessMessage â†’ handler",
				instrumentationNotes = new[]
				{
					"HTTP client injects LabTrace traceparent (same convention as Exp 8) — not RabbitMQ propagation.",
					"EventBus.StartActivity creates ActivitySource('EventBus').StartActivity('ProcessMessage') with no parent ActivityContext from headers.",
					"RabbitMQ BasicProperties headers carry event type / occurred-on / source / x-retry-count only — no traceparent.",
					"OutBoxWorker and PublishDirect emit no ActivitySource spans today."
				}
			},
			origin = new
			{
				httpStatus = httpResult.HttpStatus,
				orderId = httpResult.OrderId,
				clientDurationMs = httpResult.ClientDurationMs,
				traceId = originTraceHex,
				spanCount = originSpans.Count,
				hasAspNetCore = originHasAspNet,
				hasMediator = originHasMediator,
				hasNpgsql = originHasNpgsql,
				eventBusProcessMessageOnOriginTrace = originEventBusSpans.Count,
				sources = originSpans.Select(s => s.Source).Distinct().OrderBy(x => x).ToArray()
			},
			asyncBoundary = new
			{
				integrationEventId = delivered.Id,
				orderId = delivered.OrderId,
				outboxMessageId = outbox.OutboxMessageId,
				outboxStatus = outbox.Status,
				inboxRowCount = inbox.InboxRowCount,
				inboxIsProcessed = inbox.IsProcessed,
				handlerObservationsForOrder = _fixture.ProcessedEvents.Count(e => e.OrderId == httpResult.OrderId),
				outboxWorkerSpanCount = outboxWorkerSpans.Count,
				rabbitPublishSpanCount = rabbitPublishSpans.Count,
				processMessageSpans = processSpansForMessage.Select(s => new
				{
					s.TraceId,
					s.SpanId,
					s.ParentSpanId,
					linkCount = s.Links.Count,
					links = s.Links,
					messageId = GetTag(s, "message.id"),
					messageStatus = GetTag(s, "message.status")
				}).ToList()
			},
			correlation = new
			{
				characterization,
				w3cTraceContextCrossesRabbitBoundary = w3cCrossesRabbitBoundary,
				consumerSharesOriginTraceId = consumerSharesOriginTrace,
				consumerParentSpanOnOriginTrace = consumerHasParentOnOrigin,
				consumerActivityLinksToOrigin = consumerLinksToOrigin,
				distinctConsumerTraceIds = consumerTraceIds,
				businessIdCorrelation = new
				{
					outboxIdEqualsIntegrationEventId = outbox.OutboxMessageId == delivered.Id,
					processMessageTaggedWithIntegrationEventId = processSpansForMessage.All(s => HasMessageId(s, delivered.Id)),
					handlerOrderIdMatchesHttpOrderId = delivered.OrderId == httpResult.OrderId
				}
			},
			notes = new[]
			{
				"If characterization is SEPARATE_CONSUMER_TRACE_BUSINESS_ID_CORRELATION_ONLY, Exp 18 documents the gap — it does not fix propagation.",
				"Correlate async path via IntegrationEvent.Id (== outbox Id == RabbitMQ MessageId == ProcessMessage message.id tag) and OrderId.",
				$"Origin TraceId={originTraceHex}; consumer TraceId(s)=[{string.Join(",", consumerTraceIds)}]."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		// Originating operation
		httpResult.HttpStatus.Should().Be(200);
		originHasMediator.Should().BeTrue("HTTP cache-miss order create should run Mediator on the originating TraceId");
		originHasNpgsql.Should().BeTrue("HTTP cache-miss should touch Npgsql on the originating TraceId");
		httpResult.CachedResponseHeader.Should().BeFalse();

		// Outbox + handler once
		outbox.OutboxMessageId.Should().Be(delivered.Id);
		outbox.OrderId.Should().Be(httpResult.OrderId);
		_fixture.ProcessedEvents.Count(e => e.OrderId == httpResult.OrderId).Should().Be(1);
		inbox.InboxRowCount.Should().BeGreaterThanOrEqualTo(1);
		inbox.IsProcessed.Should().BeTrue();

		// Consumer processing occurred
		processSpansForMessage.Should().NotBeEmpty(
			"EventBus ProcessMessage Activity tagged with message.id={0} should be captured", delivered.Id);
		processSpansForMessage.Should().OnlyContain(s =>
			string.Equals(GetTag(s, "message.status"), "processed", StringComparison.Ordinal));

		// Business-id correlation across the async boundary
		processSpansForMessage.Should().OnlyContain(s => HasMessageId(s, delivered.Id));
		delivered.OrderId.Should().Be(httpResult.OrderId);

		// Characterize W3C vs separate consumer trace (do not force propagation)
		originEventBusSpans.Should().BeEmpty(
			"ProcessMessage should not appear on the HTTP originating TraceId when the consumer starts its own Activity without parent context from RabbitMQ headers");
		consumerSharesOriginTrace.Should().BeFalse(
			"current EventBus.StartActivity does not continue the HTTP TraceId across RabbitMQ");
		consumerHasParentOnOrigin.Should().BeFalse();
		consumerLinksToOrigin.Should().BeFalse();
		w3cCrossesRabbitBoundary.Should().BeFalse(
			"W3C trace context does not cross the RabbitMQ boundary in current production instrumentation");
		consumerTraceIds.Should().NotBeEmpty();
		consumerTraceIds.Should().NotContain(originTraceHex);

		characterization.Should().Be("SEPARATE_CONSUMER_TRACE_BUSINESS_ID_CORRELATION_ONLY");
	}

	private async Task WaitForInboxProcessedAsync(Guid messageId)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
		{
			var state = await QueryInboxAsync(messageId);
			if (state.InboxRowCount >= 1 && state.IsProcessed)
				return;
			await Task.Delay(100);
		}

		throw new TimeoutException($"Inbox message {messageId} was not marked processed within timeout");
	}

	private static OutboxSnapshot ToSnapshot(OrderOutboxRow row)
		=> new(
			row.OutboxMessageId,
			row.IntegrationEventId,
			row.OrderId,
			row.Status,
			row.WorkerPending,
			row.WorkerProcessed);

	private async Task<InboxSnapshot> QueryInboxAsync(Guid messageId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var rows = await db.InboxMessages.AsNoTracking().Where(m => m.Id == messageId).ToListAsync();
		var row = rows.SingleOrDefault();
		return new InboxSnapshot(rows.Count, row?.IsProcessed ?? false, row?.Status.ToString());
	}

	private static bool IsEventBusProcessMessage(CapturedActivity span)
		=> span.Source == "EventBus" && span.DisplayName == "ProcessMessage";

	private static bool HasMessageId(CapturedActivity span, Guid messageId)
		=> span.Tags.TryGetValue("message.id", out var mid)
			&& string.Equals(mid, messageId.ToString(), StringComparison.Ordinal);

	private static string? GetTag(CapturedActivity span, string key)
		=> span.Tags.TryGetValue(key, out var value) ? value : null;

	private sealed record OutboxSnapshot(
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		string Status,
		bool WorkerPending,
		bool WorkerProcessed);

	private sealed record InboxSnapshot(int InboxRowCount, bool IsProcessed, string? Status);
}
