using FluentValidation;

namespace FeatureFusion.Models.Validator
{
	public abstract class BaseValidator<TModel> : AbstractValidator<TModel> where TModel : class
	{
		protected BaseValidator()
		{
			PostInitialize();
		}
		/// <summary>
		/// you can override this method in custom partial classes in order to add some custom initialization code to constructors
		/// </summary>
		protected virtual void PostInitialize()
		{
		}
	}
}