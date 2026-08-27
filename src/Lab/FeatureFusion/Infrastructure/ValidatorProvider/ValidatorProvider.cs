using FeatureFusion.Infrastructure.ValidationProvider;
using FeatureFusion.Models.Validator;
using FluentValidation;
using System.Collections.Concurrent;
using System.Reflection;


	/// <summary>
	/// Provides and caches validators for model types dynamically.
	/// </summary>
	public class ValidatorProvider : IValidatorProvider
	{
		private readonly IEnumerable<IValidator> _registeredValidators;
		private readonly ConcurrentDictionary<Type, IValidator> _validatorCache = new();

		/// <summary>
		/// Initializes the provider with a collection of registered validators.
		/// </summary>
		/// <param name="registeredValidators">The available validators.</param>
		public ValidatorProvider(IEnumerable<IValidator> registeredValidators)
		{
			_registeredValidators = registeredValidators;
		}

		/// <summary>
		/// Gets a validator for the specified model type.
		/// </summary>
		/// <typeparam name="TModel">The model type.</typeparam>
		/// <returns>The corresponding validator.</returns>
		public IValidator GetValidator<TModel>() => GetValidatorForType(typeof(TModel));

		/// <summary>
		/// Gets a validator for a given model type.
		/// </summary>
		/// <param name="modelType">The model type.</param>
		/// <returns>The corresponding validator.</returns>
		public IValidator GetValidatorForType(Type modelType) => _validatorCache.GetOrAdd(modelType, LocateValidatorForType);

		/// <summary>
		/// Finds a validator for the given model type.
		/// </summary>
		/// <param name="modelType">The model type.</param>
		/// <returns>The validator or <c>null</c> if none found.</returns>
		/// <exception cref="InvalidOperationException">Thrown if multiple validators exist for the same type.</exception>
		private IValidator LocateValidatorForType(Type modelType)
		{
			var validatorType = CreateValidatorTypeForModel(modelType);

			var matchingValidators = _registeredValidators
				.Where(v => validatorType.GetTypeInfo().IsInstanceOfType(v))
				.ToArray();

			if (matchingValidators.Length > 1)
			{
				var names = string.Join(", ", matchingValidators.Select(v => v.GetType().Name));
				throw new InvalidOperationException($"Multiple validators found for '{modelType.Name}': {names}.");
			}

			return matchingValidators.FirstOrDefault();
		}

		/// <summary>
		/// Constructs the expected validator type for a given model.
		/// </summary>
		/// <param name="modelType">The model type.</param>
		/// <returns>The corresponding validator type.</returns>
		private static Type CreateValidatorTypeForModel(Type modelType) =>
			typeof(AbstractValidator<>).MakeGenericType(modelType);
	}

