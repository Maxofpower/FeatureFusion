using System.Text.Json.Serialization;

namespace FeatureFusion.Dtos
{
	public record ProductPromotionDto
	{
		[JsonPropertyName("product_id")]
		public int ProductId { get; init; }
		[JsonPropertyName("product_name")]
		public string Name { get; init; }
		[JsonPropertyName("manufacturer_id")]
		public int ManufacturerId { get; init; }
		[JsonPropertyName("is_featured")]
		public bool IsFeatured { get; init; }
	}
}
