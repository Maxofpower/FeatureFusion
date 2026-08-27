using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace EventBusRabbitMQ.Infrastructure.Messaging
{
	public interface IMessageDeduplicationService
	{
		Task<bool> IsDuplicateAsync(Guid messageId);
		Task<bool> MarkAsProcessedAsync(Guid messageId);
	}
}