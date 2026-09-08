using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Features.Admission;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Admission;

/// <summary>
/// CreateOrder + Admission persistence: Defer creates no Order; Release creates one Order;
/// concurrent release still single-winner.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CreateOrderAdmissionPersistenceTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly HttpClient _http;

	public CreateOrderAdmissionPersistenceTests(AspireFixture fixture)
	{
		_fixture = fixture;
		var host = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.PostConfigure<CapabilityAdmissionOptions>(o =>
				{
					o.DeferredCapabilities.Clear();
					o.DeferredCapabilities.Add(CapabilityIds.OrdersCreate);
					o.TicketTtl = TimeSpan.FromHours(1);
				});
			});
		});
		_http = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	}

	[Fact]
	public async Task Defer_does_not_insert_domain_order()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var before = await db.Orders.CountAsync();

		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(_http, capture, Ulid.NewUlid().ToString(), quantity: 1);
		result.HttpStatus.Should().Be((int)HttpStatusCode.Accepted);

		(await db.Orders.CountAsync()).Should().Be(before);
	}

	[Fact]
	public async Task Release_persists_exactly_one_domain_order()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var before = await db.Orders.CountAsync();

		using var capture = new InProcessActivityCapture();
		var create = await HttpOrderCreate.PostAsync(_http, capture, Ulid.NewUlid().ToString(), quantity: 1);
		var ticketId = JsonSerializer.Deserialize<PendingDto>(create.Body, JsonOptions)!.TicketId;

		using var release = await _http.PostAsync($"/api/v1/admission/tickets/{ticketId}/release", null);
		release.StatusCode.Should().Be(HttpStatusCode.OK);
		var released = await release.Content.ReadFromJsonAsync<ReleaseDto>(JsonOptions);
		released.Should().NotBeNull();
		released!.OrderId.Should().NotBeEmpty();

		(await db.Orders.CountAsync()).Should().Be(before + 1);

		var createdId = await db.Orders.AsNoTracking()
			.OrderByDescending(o => (int)o.Id)
			.Select(o => (int)o.Id)
			.FirstAsync();
		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, createdId);
	}

	[Fact]
	public async Task Concurrent_release_persists_single_order()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var before = await db.Orders.CountAsync();

		using var capture = new InProcessActivityCapture();
		var create = await HttpOrderCreate.PostAsync(_http, capture, Ulid.NewUlid().ToString(), quantity: 1);
		var ticketId = JsonSerializer.Deserialize<PendingDto>(create.Body, JsonOptions)!.TicketId;

		var tasks = Enumerable.Range(0, 2)
			.Select(_ => _http.PostAsync($"/api/v1/admission/tickets/{ticketId}/release", null))
			.ToArray();
		var results = await Task.WhenAll(tasks);
		var statuses = results.Select(r => r.StatusCode).ToList();
		statuses.Should().Contain(HttpStatusCode.OK);
		statuses.Should().Contain(HttpStatusCode.Conflict);

		(await db.Orders.CountAsync()).Should().Be(before + 1);

		var createdId = await db.Orders.AsNoTracking()
			.OrderByDescending(o => (int)o.Id)
			.Select(o => (int)o.Id)
			.FirstAsync();
		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, createdId);
	}

	private sealed record PendingDto(Guid TicketId);
	private sealed record ReleaseDto(Guid? OrderId, Guid TicketId);
}
