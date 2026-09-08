namespace BuildingBlocks.Domain;

/// <summary>Thrown when a factory or domain method rejects invalid state.</summary>
public class DomainException : Exception
{
	/// <summary>Creates a domain exception.</summary>
	/// <param name="message">Why the operation was rejected.</param>
	public DomainException(string message) : base(message)
	{
	}

	/// <summary>Creates a domain exception with an inner cause.</summary>
	public DomainException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}