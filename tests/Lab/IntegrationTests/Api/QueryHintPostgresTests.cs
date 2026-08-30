using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Pagination;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Api;

/// <summary>
/// Postgres execute: <see cref="QueryHint.ReadUncommitted"/> is a no-op (lab catalogdb).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class QueryHintPostgresTests
{
	private readonly AspireFixture _fixture;

	public QueryHintPostgresTests(AspireFixture fixture) => _fixture = fixture;

	[Fact]
	public async Task QueryHint_ReadUncommitted_On_Postgres_Is_Noop()
	{
		using var scope = _fixture.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		db.Database.ProviderName.Should().Contain("Npgsql");

		var page = await db.Product
			.AsNoTracking()
			.ToCursorPageAsync(
				new CursorRequest(null, 5),
				ProductSortKeys.IdAsc,
				new PaginationOptions { Hint = QueryHint.ReadUncommitted });

		page.Items.Should().NotBeEmpty();
		db.Database.CurrentTransaction.Should().BeNull();
	}
}
