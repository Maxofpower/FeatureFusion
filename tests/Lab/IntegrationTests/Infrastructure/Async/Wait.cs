using System.Diagnostics;

namespace IntegrationTests.Infrastructure.Async;

public static class Wait
{
	public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (condition()) return;
			await Task.Delay(100);
		}
		throw new TimeoutException("Condition not met within timeout");
	}
}
