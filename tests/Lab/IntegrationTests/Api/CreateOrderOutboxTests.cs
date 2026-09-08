using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Api;

/// <summary>Outbox row is written in the same commit as CreateOrder.</summary>
[Collection(AspireCollection.Name)]
public sealed class CreateOrderOutboxTests
{
	private readonly AspireFixture _fixture;
	private readonly HttpClient _http;

	public CreateOrderOutboxTests(AspireFixture fixture)
	{
		_fixture = fixture;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	}

	[Fact]
	public async Task Successful_create_writes_ordercreated_outbox_message()
	{
		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: 13, customerId: 10);
		result.HttpStatus.Should().Be(200);

		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var messages = await db.OutboxMessages.AsNoTracking()
			.Where(m => m.EventType.Contains(nameof(OrderCreatedIntegrationEvent)))
			.OrderByDescending(m => m.CreatedAt)
			.Take(5)
			.ToListAsync();

		messages.Should().NotBeEmpty();
		var payloadText = messages
			.Where(m => m.Payload is { Length: > 0 })
			.Select(m => System.Text.Encoding.UTF8.GetString(m.Payload!));
		payloadText.Should().Contain(p => p.Contains(result.OrderId.ToString(), StringComparison.OrdinalIgnoreCase));

		var body = System.Text.Json.JsonSerializer.Deserialize<CreateOrderBody>(
			result.Body,
			new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (body is not null)
			await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, body.DomainOrderId);
	}

	private sealed record CreateOrderBody(int DomainOrderId);
}
