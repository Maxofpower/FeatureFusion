using System.ComponentModel.DataAnnotations;

namespace FeatureFusion.Domain.Entities
{
	public abstract partial record BaseEntity
	{
		[Key]
		public int Id { get; set; }
	}
}
