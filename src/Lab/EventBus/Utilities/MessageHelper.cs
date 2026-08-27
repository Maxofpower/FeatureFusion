using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventBusRabbitMQ.Utilities;

/// <summary>
/// Helpers for configuring RabbitMQ message properties.
/// </summary>
public static class MessageHelper
{
	/// <summary>
	/// Configures delivery properties for an integration event.
	/// </summary>
	public static void ConfigureBasicProperties(
		BasicProperties properties,
		IntegrationEvent @event,
		string serviceName)
	{
		properties.DeliveryMode = DeliveryModes.Persistent;
		properties.MessageId = @event.Id.ToString();
		properties.Headers = new Dictionary<string, object?>
		{
			[RabbitMQConstants.EventTypeHeader] = @event.GetType().Name,
			[RabbitMQConstants.OccurredOnHeader] = @event.CreationDate.ToString("O"),
			[RabbitMQConstants.SourceServiceHeader] = serviceName,
			[RabbitMQConstants.RetryCountHeaderKey] = 0,
		};
	}

	/// <summary>
	/// Reads the message id from delivery args.
	/// </summary>
	public static Guid GetMessageId(BasicDeliverEventArgs args) =>
		Guid.Parse(args.BasicProperties.MessageId ?? throw new InvalidOperationException("MessageId is required"));

	/// <summary>
	/// Reads the retry count from delivery headers.
	/// </summary>
	public static int GetRetryCount(BasicDeliverEventArgs args)
	{
		if (args.BasicProperties.Headers?.TryGetValue(RabbitMQConstants.RetryCountHeaderKey, out var value) == true)
		{
			return value switch
			{
				int count => count,
				long l => (int)l,
				byte[] bytes when bytes.Length > 0 => bytes[0],
				_ => 0,
			};
		}

		return 0;
	}
}

/// <summary>
/// Thrown when a published message is not acknowledged by the broker within the confirm timeout.
/// </summary>
public sealed class MessageNotAckedException : Exception
{
	/// <summary>Gets the message identifier.</summary>
	public Guid MessageId { get; }

	/// <summary>
	/// Creates a new <see cref="MessageNotAckedException"/>.
	/// </summary>
	public MessageNotAckedException(Guid messageId)
		: base($"Message {messageId} was not acknowledged by broker")
	{
		MessageId = messageId;
	}
}

/// <summary>
/// EF Core exception helpers.
/// </summary>
public static class DbExceptionExtensions
{
	/// <summary>
	/// Returns true when the exception represents a PostgreSQL duplicate key violation.
	/// </summary>
	public static bool IsDuplicateKeyError(this DbUpdateException ex) =>
		ex.InnerException is PostgresException { SqlState: "23505" };
}

/// <summary>
/// Domain/business failure that should not be retried.
/// </summary>
public class BusinessException : Exception
{
	/// <summary>Creates a new business exception.</summary>
	public BusinessException(string message) : base(message) { }
}

/// <summary>
/// Transient failure that may be retried.
/// </summary>
public class TransientException : Exception
{
	/// <summary>Creates a new transient exception.</summary>
	public TransientException(string message) : base(message) { }

	/// <summary>Creates a new transient exception with an inner exception.</summary>
	public TransientException(string message, Exception inner) : base(message, inner) { }
}
