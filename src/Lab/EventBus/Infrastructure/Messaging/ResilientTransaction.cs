using Microsoft.EntityFrameworkCore;

public class ResilientTransaction
{
	private readonly DbContext _context;

	private ResilientTransaction(DbContext context) =>
		_context = context ?? throw new ArgumentNullException(nameof(context));

	public static ResilientTransaction New(DbContext context) => new(context);

	public async Task ExecuteAsync(Func<Task> action)
	{
		var strategy = _context.Database.CreateExecutionStrategy();
		await strategy.ExecuteAsync(async () =>
		{
			// Using NoTracking since we're just checking existence
			await using var transaction = await _context.Database.BeginTransactionAsync();
			try
			{
				await action();
				await transaction.CommitAsync();
			}
			catch
			{
				await transaction.RollbackAsync();
				throw;
			}
		});
	}

	public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
	{
		var strategy = _context.Database.CreateExecutionStrategy();
		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await _context.Database.BeginTransactionAsync();
			try
			{
				var result = await action();
				await transaction.CommitAsync();
				return result;
			}
			catch
			{
				await transaction.RollbackAsync();
				throw;
			}
		});
	}
}