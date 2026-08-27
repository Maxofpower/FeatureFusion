public readonly struct Result<T>
{
	private readonly T _value;
	private readonly string _error = string.Empty;
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