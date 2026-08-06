using FeatureFusion.Domain.Entities;
using FeatureFusion.Dtos;

namespace FeatureFusion.Infrastructure.Extensions
{
	public static class ProductExtensions
	{
		public static ProductDto ToDto(this Product product) => new(
			product.Id,
			product.Name,
			product.Price,
			product.FullDescription,
			product.CreatedAt);

		public static List<ProductDto> ToDtos(this IEnumerable<Product> products) =>
			products.Select(p => p.ToDto()).ToList();
	}
}
