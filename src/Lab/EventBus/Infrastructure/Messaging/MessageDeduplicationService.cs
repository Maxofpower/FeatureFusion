using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure.Context;
using EventBusRabbitMQ.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;


public class MessageDeduplicationService : IMessageDeduplicationService
{
	private readonly IDbContextFactory<EventBusDbContext> _contextFactory;
	private readonly TimeSpan _deduplicationWindow = TimeSpan.FromDays(1);
	private readonly ILogger<MessageDeduplicationService> _logger;

	public MessageDeduplicationService(IDbContextFactory<EventBusDbContext> contextFactory, ILogger<MessageDeduplicationService> logger)
	{
		_contextFactory = contextFactory;
		_logger = logger;
	}

	public async Task<bool> IsDuplicateAsync(Guid messageId)
	{
		await using var _dbContext = _contextFactory.CreateDbContext();
		var cutoff = DateTime.UtcNow - _deduplicationWindow;

		return await _dbContext.ProcessedMessages
			.AsNoTracking()
			.AnyAsync(x => x.Id == messageId && x.ProcessedAt >= cutoff);
	}

	public async Task<bool> MarkAsProcessedAsync(Guid messageId)
	{
		await using var _dbContext = _contextFactory.CreateDbContext();
		try
		{
			return await ResilientTransaction.New(_dbContext).ExecuteAsync<bool>(async () =>
			{
				var exists = await _dbContext.ProcessedMessages
					.AsNoTracking()
					.AnyAsync(x => x.Id == messageId);

				if (exists) return false;

				_dbContext.ProcessedMessages.Add(new ProcessedMessage
				{
					Id = messageId,
					ProcessedAt = DateTime.UtcNow
				});

				await _dbContext.SaveChangesAsync();
				return true;
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to mark message {MessageId} as processed", messageId);
			throw;
		}
	}
}

