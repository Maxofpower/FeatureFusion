namespace FeatureFusion.Domain.Entities
{
	public record ProductManufacturer
	{
		public int ProductId { get; init; }
		public int ManufacturerId { get; init; }
		public bool IsFeaturedProduct { get; init; }
	}
}
