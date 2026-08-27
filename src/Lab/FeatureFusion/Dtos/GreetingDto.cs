using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Dtos
{
	public class GreetingDto
	{
		[FromHeader]
		public required string Fullname { get; set; }
	}
}
