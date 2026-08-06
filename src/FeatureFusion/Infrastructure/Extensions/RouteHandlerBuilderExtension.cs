using FeatureFusion.Infrastructure.ValidationProvider;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

public static class RouteHandlerBuilderExtensions
{
	/// <summary>
	/// Adds model validation to an endpoint using FluentValidation.
	/// If a validator for <typeparamref name="TModel"/> exists, it validates the request before executing the handler.
	/// If validation fails, it returns a <see cref="Results.ValidationProblem"/> response.
	/// </summary>
	public static RouteHandlerBuilder WithValidation<TModel>(this RouteHandlerBuilder builder)
	{
		return builder.AddEndpointFilter(async (context, next) =>
		{
			var requestModel = context.Arguments.OfType<TModel>().FirstOrDefault();
			if (requestModel == null) return await next(context);

			// Resolving from DI is preferred to keep dependencies managed and avoid manual injection.
			var validatorProvider = context.HttpContext.RequestServices.GetRequiredService<IValidatorProvider>();
			var validator = validatorProvider.GetValidator<TModel>();

			if (validator == null) return await next(context);

			var validationResult = await validator.ValidateAsync(new ValidationContext<TModel>(requestModel));

			return validationResult.IsValid
				? await next(context)
				: Results.ValidationProblem(validationResult.ToDictionary());
		});
	}
}
