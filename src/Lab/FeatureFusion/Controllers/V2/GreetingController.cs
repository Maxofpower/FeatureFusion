using FeatureFusion.Dtos;
using FeatureFusion.Infrastructure.Filters;
using FeatureFusion.Models;
using FeatureFusion.Services.FeatureToggleService;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Controllers.V2

{

	[ApiController]
	[Route("api/v{version:apiVersion}/[controller]")]
	public class GreetingController : ControllerBase
	{
		private readonly IFeatureManagerSnapshot _featureManager;
		private readonly GreetingValidator _validator;
		private readonly IFeatureToggleService _featureToggleService;


		public GreetingController(IFeatureManagerSnapshot featureManager,
			GreetingValidator validator
			, IFeatureToggleService featureToggleService)
		{

			_featureManager = featureManager;
			_validator = validator;
			_featureToggleService = featureToggleService;
		}

		//leveraging coR pattern with some static rules
		[HttpPost("custom-greeting")]
		[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))] // Ok<string>
		[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))] // BadRequest<ValidationProblemDetails>
		[ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))] // NotFound<string>	
		public async Task<Results<Ok<string>, BadRequest<ValidationProblemDetails>, NotFound<string>>> GetCustomGreeting(GreetingDto greeting)
		{

			var validationResult = await _validator.ValidateWithResultAsync(greeting);

			if (!validationResult.IsValid)
			{

				return TypedResults.BadRequest(validationResult.ProblemDetails);
			}

			//for testing purpose -  a static customer
			var user = new UserDto("Admin", true, false);

			bool greetingAccess = await _featureToggleService.CanAccessFeatureAsync(user); // ? Evaluates all rules

			if (greetingAccess)
			{
				return TypedResults.Ok($"Hello VIP user {greeting.Fullname}, this is your custom greeting V2!");
			}

			return TypedResults.Ok("Hello Anonymous user V2!");
		}
		
	}

}
	

