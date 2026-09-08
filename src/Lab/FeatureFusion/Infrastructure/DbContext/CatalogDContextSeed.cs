using FeatureFusion.Infrastructure.Extensions;
using FeatureFusion.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FeatureFusion.Infrastructure.Context;

public partial class CatalogDContextSeed(
	IWebHostEnvironment env,
	ILogger<CatalogDContextSeed> logger) : IDbSeeder<CatalogDbContext>
{
	public async Task SeedAsync(CatalogDbContext context)
	{
		_ = env;

		context.Database.OpenConnection();
		((NpgsqlConnection)context.Database.GetDbConnection()).ReloadTypes();

		await DemoCommerceSeed.SeedAsync(context, logger).ConfigureAwait(false);
	}
}
