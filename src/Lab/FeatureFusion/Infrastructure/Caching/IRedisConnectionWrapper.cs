using StackExchange.Redis;
using System.Net;

namespace FeatureFusion.Infrastructure.Caching
{
	public partial interface IRedisConnectionWrapper
	{
		/// <summary>
		/// Obtain an interactive connection to a database inside Redis
		/// </summary>
		/// <returns>Redis cache database</returns>
		Task<IDatabase> GetDatabaseAsync();

		/// <summary>
		/// Obtain an interactive connection to a database inside Redis
		/// </summary>
		/// <returns>Redis cache database</returns>
		IDatabase GetDatabase();

		/// <summary>
		/// Obtain a configuration API for an individual server
		/// </summary>
		/// <param name="endPoint">The network endpoint</param>
		/// <returns>Redis server</returns>
		Task<IServer> GetServerAsync(EndPoint endPoint);

		/// <summary>
		/// Gets all endpoints defined on the server
		/// </summary>
		/// <returns>Array of endpoints</returns>
		Task<EndPoint[]> GetEndPointsAsync();

		/// <summary>
		/// Gets a subscriber for the server
		/// </summary>
		/// <returns>Array of endpoints</returns>
		Task<ISubscriber> GetSubscriberAsync();

		/// <summary>
		/// Gets a subscriber for the server
		/// </summary>
		/// <returns>Array of endpoints</returns>
		ISubscriber GetSubscriber();

		/// <summary>
		/// Delete all the keys of the database
		/// </summary>
		Task FlushDatabaseAsync();


		/// <summary>
		/// Acquire a distributed lock.
		/// </summary>
		/// <returns>True if the lock was acquired, false otherwise.</returns>
	     Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry);


		/// <summary>
		/// Releases a distributed lock.
		/// </summary>
		/// <returns>True if the lock was released, false otherwise.</returns>
		 Task<bool> ReleaseLockAsync(string key, string value);
		

		/// <summary>
		/// The Redis instance name
		/// </summary>
		string Instance { get; }
	}
}
