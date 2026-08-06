using FeatureFusion.Domain.Entities;
using FeatureFusion.Infrastructure.Extensions;
using FeatureManagementFilters.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text.Json;

namespace FeatureFusion.Infrastructure.Context;

public partial class CatalogDContextSeed(
	IWebHostEnvironment env,
	ILogger<CatalogDContextSeed> logger) : IDbSeeder<CatalogDbContext>
{
	public async Task SeedAsync(CatalogDbContext context)
	{
		var contentRootPath = env.ContentRootPath;
		var picturePath = env.WebRootPath;
	
	
		context.Database.OpenConnection();
		((NpgsqlConnection)context.Database.GetDbConnection()).ReloadTypes();
		

		if (!context.Product.Any())
		{

			var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Setup", "catalog.json");
			var sourceJson = File.ReadAllText(sourcePath);
			var sourceItems = JsonSerializer.Deserialize<CatalogSourceEntry[]>(sourceJson);

			await context.SaveChangesAsync();		

			var catalogItems = sourceItems.Select(source => new Product
			{
				Id = source.Id,
				Name = source.Name,
				Price = source.Price,
				CreatedAt=source.CreatedAt
			
			}).ToArray();

			await context.Product.AddRangeAsync(catalogItems);
			logger.LogInformation("Seeded catalog with {NumItems} items", context.Product.Count());
			await context.SaveChangesAsync();
		}
	}

	private class CatalogSourceEntry
	{
		public int Id { get; set; }
		public string Type { get; set; }
		public string Brand { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public decimal Price { get; set; }
		public DateTime CreatedAt { get; set; }
	}
}
