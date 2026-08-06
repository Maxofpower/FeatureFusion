using FeatureFusion.Domain.Entities;
using FeatureFusion.Dtos;
using FeatureFusion.Features.Products.Queries;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Infrastructure.Extensions;
using FeatureManagementFilters.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Polly;
using System.Collections.Generic;
using System.Linq.Expressions;
namespace FeatureManagementFilters.Services.ProductService
{
	public interface IProductService
	{
		Task<IList<ProductPromotionDto>> GetProductPromotionAsync(bool getFromMemCach, CancellationToken cancellationToken = default);
		Task<List<ProductPromotionDto>> GetProductRocemmendationAsync(CancellationToken cancellationToken = default);
		Task<Result<PagedResult<ProductDto>>> GetProductsAsync(int limit,
		ProductSortField sortField,
		SortDirection sortDirection,
		string cursor,
		CancellationToken cancellationToken);
	}

	public class ProductService : IProductService
	{
		protected readonly IStaticCacheManager _staticCacheManager;
		private readonly IDistributedCacheManager _distributedCacheManager;
		private readonly CatalogDbContext _dbContext;
		private readonly ILogger<ProductService> _logger;

		public ProductService(IStaticCacheManager staticCacheManager,
			IDistributedCacheManager distributedCacheManager,
			CatalogDbContext dbContext,
			ILogger<ProductService> logger)
		{
			_staticCacheManager = staticCacheManager;
			_distributedCacheManager = distributedCacheManager;
			_dbContext = dbContext;
			_logger = logger;
		}

		public async Task<IList<ProductPromotionDto>> GetProductPromotionAsync(bool getFromMemCach = false, CancellationToken cancellationToken = default)
		{
			IList<ProductPromotionDto> productPromotion = new List<ProductPromotionDto>();
			try
			{
				var cacheKey = new CacheKey("Promotion.BlackFriday");
				//	Console.WriteLine("==> Trying to get data from the cache for key: Promotion.BlackFriday...");

				// Check if cancellation is requested before starting any operation
				cancellationToken.ThrowIfCancellationRequested();

				if (getFromMemCach)
				{
					productPromotion = await _distributedCacheManager.GetValueOrCreateAsync(cacheKey, async () =>
					{
						var products = await GenerateSampleData();
						return products;
					});
				}
				else
				{
					productPromotion = await _staticCacheManager.GetAsync(cacheKey, async () =>
					{
						//	Console.WriteLine("==> Cache miss for key: Promotion.BlackFriday. Fetching data from the source...");
						// Static data representing products and their manufacturer promotions
						var products = await GenerateSampleData();
						return products;
					});
				}
				// i return result here for debug purpose , for production appInitilizer there is no need to return data

			}
			catch
			{
				// ignore
			}
			return productPromotion;
		}
		public Task<List<ProductPromotionDto>> GetProductRocemmendationAsync(CancellationToken cancellationToken = default)
		{
			try
			{
				// Check if cancellation is requested before starting any operation
				cancellationToken.ThrowIfCancellationRequested();

				// Static data representing products and their manufacturer promotions
				var products =
				new List<Product>{new Product { Id=1, Name = "Laptop", Published = true, Deleted = false, VisibleIndividually = true },
					new Product { Id=2,Name = "Phone", Published = true, Deleted = false, VisibleIndividually = true },
						new Product { Id=3, Name = "Headphones", Published = false, Deleted = false, VisibleIndividually = true }
				};

				var productManufacturers = new List<ProductManufacturer>
					{
						new ProductManufacturer { ProductId = 1, ManufacturerId = 10, IsFeaturedProduct = true },
						new ProductManufacturer { ProductId = 2 ,ManufacturerId = 20, IsFeaturedProduct = true },
						new ProductManufacturer { ProductId = 3, ManufacturerId = 10, IsFeaturedProduct = false }
					};

				// Filtering and projecting product promotions based on the static data
				var query = from p in products
							join pm in productManufacturers on p.Id equals pm.ProductId
							where p.Published && !p.Deleted && p.VisibleIndividually &&
								  pm.IsFeaturedProduct
							select new ProductPromotionDto
							{
								ProductId = p.Id,
								Name = p.Name,
								ManufacturerId = pm.ManufacturerId,
								IsFeatured = pm.IsFeaturedProduct
							};

				return Task.FromResult(query.ToList());  // Return the filtered data

			}
			catch
			{
				throw;
			}
		}

		public ValueTask<IList<ProductPromotionDto>> GenerateSampleData()
		{
			// Static data representing products and their manufacturer promotions
			var products = new List<Product>
					{
					new Product {  Name = "Laptop", Published = true, Deleted = false, VisibleIndividually = true },
					new Product {  Name = "Phone", Published = true, Deleted = false, VisibleIndividually = true },
					new Product {  Name = "Headphones", Published = false, Deleted = false, VisibleIndividually = true }
					};

			var productManufacturers = new List<ProductManufacturer>
					{
					new ProductManufacturer { ProductId = 1, ManufacturerId = 10, IsFeaturedProduct = true },
					new ProductManufacturer { ProductId = 2, ManufacturerId = 20, IsFeaturedProduct = true },
					new ProductManufacturer { ProductId = 3, ManufacturerId = 10, IsFeaturedProduct = false }
					};

			// Filtering and projecting product promotions based on the static data
			IList<ProductPromotionDto> query = (from p in products
												join pm in productManufacturers on p.Id equals pm.ProductId
												where p.Published && !p.Deleted && p.VisibleIndividually &&
													  pm.IsFeaturedProduct
												select new ProductPromotionDto
												{
													ProductId = p.Id,
													Name = p.Name,
													ManufacturerId = pm.ManufacturerId,
													IsFeatured = pm.IsFeaturedProduct
												}).ToList();
			return ValueTask.FromResult(query);
		}
	
	public async Task<Result<PagedResult<ProductDto>>> GetProductsAsync(
	  int limit,
	  ProductSortField sortField,
	  SortDirection direction,
	  string cursor,
	  CancellationToken cancellationToken)
		{
			try
			{
				return await PaginationHelper.PaginateAsync<Product, ProductDto, ProductSortField>(
					_dbContext.Product,
					limit,
					sortField,
					direction,
					cursor,
					mapToDto: p => p.ToDto(),
					getSortFieldName: f => f.ToString(),
					parseSortField: s => Enum.Parse<ProductSortField>(s),
					cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to retrieve products");

				return Result<PagedResult<ProductDto>>.Failure(
					"An error occurred while retrieving products",
					StatusCodes.Status500InternalServerError);
			}
		}

	}
}

