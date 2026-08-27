using FluentValidation;

namespace FeatureFusion.Infrastructure.ValidationProvider
{
	public interface IValidatorProvider
	{
		/// <summary>
		/// Retrieves a validator for the specified model type <typeparamref name="TModel"/>.
		/// </summary>
		/// <typeparam name="TModel">The type of the model to validate.</typeparam>
		/// <returns>An instance of <see cref="IValidator"/> for the specified model type.</returns>
		IValidator GetValidator<TModel>();

		/// <summary>
		/// Retrieves a validator for the specified model type.
		/// </summary>
		/// <param name="modelType">The type of the model to validate.</param>
		/// <returns>An instance of <see cref="IValidator"/> for the specified model type.</returns>
		IValidator GetValidatorForType(Type type);
	}
}
