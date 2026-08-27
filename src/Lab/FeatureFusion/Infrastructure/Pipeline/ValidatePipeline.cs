using FeatureFusion.Dtos;

namespace FeatureFusion.Pipeline;

public class ValidationPipeline
{
	private Func<UserDto, Task<bool>> _pipeline = user => Task.FromResult(true); // Default: Always passes

	public void AddRule(Func<UserDto, Task<bool>> rule)
	{
		var previous = _pipeline;
		_pipeline = async user => await previous(user) && await rule(user); // Chain with AND condition
	}

	public async Task<bool> ValidateAsync(UserDto user) => await _pipeline(user);
}