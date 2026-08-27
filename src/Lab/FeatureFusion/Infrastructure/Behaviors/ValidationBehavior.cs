using BuildingBlocks.Mediator;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace FeatureFusion.Infrastructure.Behaviors;

/// <summary>
/// Host FluentValidation pipeline: runs all <see cref="IValidator{TRequest}"/>,
/// skips when none are registered, aggregates failures into <see cref="ValidationException"/>.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly IEnumerable<IValidator<TRequest>> _validators;
	private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

	public ValidationBehavior(
		IEnumerable<IValidator<TRequest>> validators,
		ILogger<ValidationBehavior<TRequest, TResponse>> logger)
	{
		_validators = validators;
		_logger = logger;
	}

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		if (!_validators.Any())
			return await next(cancellationToken).ConfigureAwait(false);

		var context = new ValidationContext<TRequest>(request);
		var failures = (await Task.WhenAll(
				_validators.Select(v => v.ValidateAsync(context, cancellationToken)))
			.ConfigureAwait(false))
			.SelectMany(r => r.Errors)
			.Where(f => f is not null)
			.ToList();

		if (failures.Count > 0)
		{
			_logger.LogWarning(
				"Validation failed for {RequestType} with {ErrorCount} error(s)",
				typeof(TRequest).Name,
				failures.Count);
			throw new ValidationException(failures);
		}

		return await next(cancellationToken).ConfigureAwait(false);
	}
}
