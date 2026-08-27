using Microsoft.AspNetCore.Mvc;

public class ValidationResult
{
	public bool IsValid { get; }
	public ValidationProblemDetails ProblemDetails { get; }

	private ValidationResult(bool isValid, ValidationProblemDetails problemDetails = null)
	{
		IsValid = isValid;
		ProblemDetails = problemDetails;
	}

	public static ValidationResult Success() =>
		new ValidationResult(true);

	public static ValidationResult Failure(ValidationProblemDetails problemDetails)
	{
		ArgumentNullException.ThrowIfNull(problemDetails);

		return new ValidationResult(false, problemDetails);
	}

	public bool HasErrors() => !IsValid && ProblemDetails != null;
}