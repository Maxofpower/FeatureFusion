using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

public readonly struct Result<T>
{
	private readonly T? _value;
	private readonly string? _error;
	private readonly int _statusCode;

	[System.Text.Json.Serialization.JsonIgnore]
	public T Value => IsSuccess ? _value! : throw new InvalidOperationException("No value for failed result");

	[System.Text.Json.Serialization.JsonIgnore]
	public string Error => !IsSuccess ? _error! : throw new InvalidOperationException("No error for successful result");

	public int StatusCode => _statusCode;
	public bool IsSuccess => _error is null;

	private Result(T value)
	{
		_value = value;
		_error = null;
		_statusCode = 0;
	}

	private Result(string error, int statusCode)
	{
		_error = error;
		_statusCode = statusCode;
		_value = default;
	}

	public static Result<T> Success(T value) => new(value);
	public static Result<T> Failure(string error, int statusCode) => new(error, statusCode);

	public TResult Match<TResult>(
		Func<T, TResult> onSuccess,
		Func<string, int, TResult> onFailure) =>
		IsSuccess ? onSuccess(_value!) : onFailure(_error!, _statusCode);
}

public static class ResultHttpExtensions
{
	public static IResult ToApiResult<T>(this Result<T> result) =>
		result.Match(
			onSuccess: static value => Results.Ok(value),
			onFailure: static (error, statusCode) => Results.Problem(
				detail: error,
				statusCode: statusCode is >= 400 and < 600 ? statusCode : StatusCodes.Status400BadRequest));

	public static Results<Ok<T>, BadRequest<ValidationProblemDetails>, ProblemHttpResult> ToHttpResult<T>(this Result<T> result)
	{
		return result.Match<Results<Ok<T>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>>(
			success => TypedResults.Ok(success),
			(error, statusCode) =>
			{
				var errors = new Dictionary<string, string[]>
				{
					{ "General", new[] { error } }
				};
				return TypedResults.BadRequest(new ValidationProblemDetails(errors)
				{
					Title = "Request Error",
					Detail = error,
					Status = statusCode
				});
			});
	}
}
