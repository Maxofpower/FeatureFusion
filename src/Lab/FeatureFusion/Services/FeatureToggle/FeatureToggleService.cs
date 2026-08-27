
using FeatureFusion.Dtos;
using FeatureFusion.Pipeline;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Services.FeatureToggleService
{
	public class FeatureToggleService : IFeatureToggleService
	{
		private readonly ValidationPipeline _validateRules ;
		private readonly IFeatureManagerSnapshot _featureManager;

		public FeatureToggleService(IFeatureManagerSnapshot featureManager)
		{
			_validateRules = new ValidationPipeline();
			_featureManager = featureManager;		
		}

		public async Task<bool> CanAccessFeatureAsync(UserDto user)
		{
			_validateRules.AddRule(async user => await _featureManager.IsEnabledAsync("CustomGreeting"));
			_validateRules.AddRule(user => Task.FromResult(user.Role == "Amin"));
			_validateRules.AddRule(user => Task.FromResult(user.HasActiveSubscription));
			return  await _validateRules.ValidateAsync(user);
		}
	}
	public interface IFeatureToggleService
	{
		Task<bool> CanAccessFeatureAsync(UserDto user);
	}
}
