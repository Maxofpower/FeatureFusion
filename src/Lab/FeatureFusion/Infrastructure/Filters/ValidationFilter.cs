using FeatureFusion.Infrastructure.ValidationProvider;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public class ValidationFilter<TModel> : IEndpointFilter
{
	private readonly IValidatorProvider _validatorProvider;

	public ValidationFilter(IValidatorProvider validatorProvider)
	{
		_validatorProvider = validatorProvider;
	}
	public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		// Find the validator for the request model
		var validator = _validatorProvider.GetValidator<TModel>();

		if (validator != null)
		{
			// Extracting the request model from the context
			var requestModel = context.Arguments.OfType<TModel>().FirstOrDefault();

			if (requestModel != null)
			{
				// Creating a validation context for the request model
				var validationContext = new ValidationContext<TModel>(requestModel);

				// Performing validation
				var validationResult = await validator.ValidateAsync(validationContext);

				if (!validationResult.IsValid)
				{
					// Returning validation errors as a problem details response
					return TypedResults.ValidationProblem(validationResult.ToDictionary());
				}
			}
		}
		// If validation passes or no validator is found, proceed to the next middleware/endpoint
		return await next(context);
	}
}