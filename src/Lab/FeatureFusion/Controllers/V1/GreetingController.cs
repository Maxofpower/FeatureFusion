using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Controllers.V1

{
	//[ApiVersion("1.0")]
	[ApiController]
	[Route("api/v{version:apiVersion}/[controller]")]

	public class GreetingController : ControllerBase
	{
		private readonly IFeatureManagerSnapshot _featureManager;

		public GreetingController(IFeatureManagerSnapshot featureManager)
		{
			_featureManager = featureManager;
		}
		//  [MapToApiVersion("1.0")]
		[HttpGet("custom-greeting")]
		public async Task<IActionResult> GetCustomGreeting()
		{
			if (await _featureManager.IsEnabledAsync("CustomGreeting"))
			{
				return Ok("Hello VIP user, this is your custom greeting!");
			}

			return Ok("Hello Anonymous user!");
		}
	}
}