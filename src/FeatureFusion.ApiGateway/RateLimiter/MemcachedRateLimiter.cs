namespace FeatureFusion.ApiGateway.RateLimiter
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Threading;
	using System.Threading.RateLimiting;
	using System.Threading.Tasks;
	using Enyim.Caching;
	using Enyim.Caching.Memcached;
	using Microsoft.AspNetCore.DataProtection.KeyManagement;
	using Microsoft.Extensions.Caching.Memory;
	using Polly;
	using Polly.Fallback;
	using Polly.Retry;


	/// <summary>
	/// A hybrid (Memcached + in-memory fallback) fixed-window rate limiter using CAS updates per partition key.
	/// </summary>
	/// <remarks>
	/// Used by the YARP ApiGateway distributed rate-limiting demo. See README (Distributed rate limiting).
	/// </remarks>
	public class MemcachedFixedWindowRateLimiter<TKey> : RateLimiter
	{
		private readonly string _partitionKey;
		private readonly MemcachedFixedWindowRateLimiterOptions _options;
		private readonly IMemcachedClient _memcachedClient;
		private int _activeRequests;
		private long _idleSince = Stopwatch.GetTimestamp();
		private readonly FixedWindowLease FailedLease = new(isAcquired: false, null);
		private readonly IMemoryCache _memoryCache;
		private readonly Lock _cacheLock = new();
		private readonly AsyncPolicy<MemcachedFixedWindowResponse> _resiliencePolicy;


		public override TimeSpan? IdleDuration => Interlocked.CompareExchange(ref _activeRequests, 0, 0) > 0
			? null
			: Stopwatch.GetElapsedTime(_idleSince);

		public MemcachedFixedWindowRateLimiter(TKey partitionKey,
			MemcachedFixedWindowRateLimiterOptions options,
			IMemcachedClient memcachedClient,
			IMemoryCache memoryCache
			)
		{
			ArgumentNullException.ThrowIfNull(options);
			if (options.PermitLimit <= 0)
				throw new ArgumentException("PermitLimit must be greater than 0.", nameof(options.PermitLimit));
			if (options.Window <= TimeSpan.Zero)
				throw new ArgumentException("Window must be greater than TimeSpan.Zero.", nameof(options.Window));

			_partitionKey = partitionKey?.ToString() ?? string.Empty;
			_options = options;
			_memcachedClient = memcachedClient ?? throw new ArgumentNullException(nameof(memcachedClient));
			_memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));

			#region we can use polly if we need more policies
			// Define the retry policy with exponential backoff.
			var retryPolicy = Policy<MemcachedFixedWindowResponse>
				.Handle<Exception>() // Handle all exceptions.
				.WaitAndRetryAsync(
					retryCount: 3, // Retry 3 times.
					sleepDurationProvider: retryAttempt =>
					{
						// Exponential backoff: 1 minute, 5 minutes, 10 minutes.
						return TimeSpan.FromMinutes(Math.Pow(2, retryAttempt - 1));
					},
					onRetry: (exception, delay, retryCount, context) =>
					{
						// Log the retry attempt.
						Console.WriteLine($"Retry {retryCount}: Waiting {delay} before next retry. Error: {exception.Exception.Message}");
					}
				);
			// Define the circuit breaker policy (non-generic).
			var circuitBreakerPolicy = Policy
				.Handle<Exception>() // Handle all exceptions.
				.CircuitBreakerAsync(
					exceptionsAllowedBeforeBreaking: 3, // Trip after 3 failures.
					durationOfBreak: TimeSpan.FromMinutes(10) // Break for 10 minutes.
				);

			// Wrap the non-generic circuit breaker policy into a generic policy.
			var genericCircuitBreakerPolicy = circuitBreakerPolicy.AsAsyncPolicy<MemcachedFixedWindowResponse>();

			// Define the fallback policy.
			var fallbackPolicy = Policy<MemcachedFixedWindowResponse>
				.Handle<Exception>() // Handle all exceptions.
				.FallbackAsync(async (ct) =>
				{
					// Fallback action: Use the in-memory cache.
					Console.Error.WriteLine("Memcached operation failed. Falling back to in-memory cache.");
					return await TryAcquireLeaseWithInMemoryCacheAsync(_activeRequests, ct);
				});

			// Combine all policies: fallback -> retry -> circuit breaker.
			_resiliencePolicy = Policy.WrapAsync(fallbackPolicy, retryPolicy, genericCircuitBreakerPolicy);

			#endregion

		}

		public override RateLimiterStatistics? GetStatistics()
		{
			// Not implemented; return null or your custom statistics.
			return null;
		}

		protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
		{
			if (permitCount > _options.PermitLimit)
			{
				throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, $"Requested {permitCount} permits, but the permit limit is {_options.PermitLimit}.");
			}

			return await AcquireAsyncCoreInternal(permitCount, cancellationToken);
		}

		protected override RateLimitLease AttemptAcquireCore(int permitCount)
		{

			return FailedLease;// FixedWindowLease;
		}

		private async ValueTask<RateLimitLease> AcquireAsyncCoreInternal(int permitCount, CancellationToken cancellationToken)
		{
			var leaseContext = new FixedWindowLeaseContext
			{
				Limit = permitCount,
				Window = _options.Window,
			};

			MemcachedFixedWindowResponse response;
			Interlocked.Increment(ref _activeRequests);
			try
			{
				response = await TryAcquireLeaseAsync(permitCount, cancellationToken);	
			}
			finally
			{
				Interlocked.Decrement(ref _activeRequests);
				_idleSince = Stopwatch.GetTimestamp();
			}

			leaseContext.Count = response.Count;
			leaseContext.RetryAfter = response.RetryAfter;
			leaseContext.ExpiresAt = response.ExpiresAt;

			return new FixedWindowLease(response.Allowed, leaseContext);
		}

		/// <summary>
		/// Try to acquire permits using Memcached CAS operations.
		/// </summary>

		private async Task<MemcachedFixedWindowResponse> TryAcquireLeaseAsync(int permitCount, CancellationToken cancellationToken)
		{
			string key = _partitionKey;  // Using _partitionKey directly for rate-limiting
			string lockKey = $"{key}:Lock";  // Lock key derived from _partitionKey
			TimeSpan lockTimeout = TimeSpan.FromSeconds(5); // Adjust the lock timeout as needed.

			try
			{
				
				// Acquire the lock.
				bool lockAcquired = await AcquireLockAsync(lockKey, lockTimeout);
				if (!lockAcquired)
				{
					// If the lock is not acquired, retry or fall back.
					Console.WriteLine("Failed to acquire lock. Retrying...");
					return await TryAcquireLeaseAsync(permitCount, cancellationToken);
				}

				//  Perform the rate-limiting logic.
				var getResult =  _memcachedClient.GetWithCas<ulong>(key);
				if (getResult.StatusCode != 0)
				{
					// Handle failed result (could be no value or other errors)
					Console.Error.WriteLine($"Error retrieving value: {getResult.StatusCode}");
					return await TryAcquireLeaseWithInMemoryCacheAsync(permitCount, cancellationToken);
				}

				ulong currentCount = getResult.Result;

				// Check if the window has expired.
				if (currentCount == 0)
				{
					// Reset the counter if the window has expired.
					currentCount = 0;
					await _memcachedClient.StoreAsync(StoreMode.Set, key, currentCount, _options.Window);
				}

				//  Increment the counter.
				ulong newCount = currentCount + (ulong)permitCount;

				//Check if the new count exceeds the limit.
				if (newCount > (ulong)_options.PermitLimit)
				{
					// Not allowed. Calculate the retry-after value.
					var expiry = DateTime.UtcNow.Add(_options.Window);
					TimeSpan retryAfter = expiry - DateTime.UtcNow;
					return CreateResponse(allowed: false, count: (int)newCount, retryAfter: retryAfter, expiry: expiry);
				}

		
				var casResult =  _memcachedClient.Cas(StoreMode.Set, key, newCount, _options.Window, getResult.Cas);
				if (!casResult.Result)
				{
					// CAS failed, the counter was modified concurrently.
					Console.WriteLine("CAS failed, retrying...");
					return await TryAcquireLeaseAsync(permitCount, cancellationToken);
				}

				// Success – return the updated count.
				return CreateResponse(allowed: true, count: (int)newCount, retryAfter: null, expiry: DateTime.UtcNow.Add(_options.Window));
			}
			catch (Exception ex)
			{
				// Log the error and fall back to in-memory cache.
				Console.Error.WriteLine($"Memcached operation error: {ex.Message}");
				return await TryAcquireLeaseWithInMemoryCacheAsync(permitCount, cancellationToken);
			}
			finally
			{
				//  Release the lock.
				await ReleaseLockAsync(lockKey);
			}
		}

		private async Task<bool> AcquireLockAsync(string lockKey, TimeSpan lockTimeout)
		{
			try
			{
				var lockValue = Guid.NewGuid().ToString();
				return await _memcachedClient.StoreAsync(StoreMode.Add, lockKey, lockValue, lockTimeout);
			}
			catch (NullReferenceException)
			{
				throw;
			}
			catch
			{
				return false;
			}
		}

		private async Task ReleaseLockAsync(string lockKey)
		{
			try
			{
				await _memcachedClient.RemoveAsync(lockKey);
			}
			catch
			{
				// Log if necessary.
			}
		}


		private MemcachedFixedWindowResponse CreateResponse(bool allowed, int count, TimeSpan? retryAfter, DateTime expiry)
		{
			return new MemcachedFixedWindowResponse
			{
				Allowed = allowed,
				Count = count,
				RetryAfter = retryAfter,
				ExpiresAt = expiry.Ticks
			};
		}
		private async ValueTask<bool> StoreAsync(string key, RateLimitData data, TimeSpan validFor)
		{
			try
			{
				// Store the data and return the result.
				bool success = await _memcachedClient.StoreAsync(StoreMode.Set, key, data, validFor);
				return success;
			}
			catch (Exception ex)
			{
				// Log the error and return false.
				Console.Error.WriteLine($"Memcached store error: {ex.Message}");
				return false;
			}
		}
		public async ValueTask<bool> RemoveAsync(string key)
		{
			try
			{
				// Store the data and return the result.
				bool success = await _memcachedClient.RemoveAsync(key);
				return success;
			}
			catch (Exception ex)
			{
				// Log the error and return false.
				Console.Error.WriteLine($"Memcached store error: {ex.Message}");
				return false;
			}
		}
		private Task<MemcachedFixedWindowResponse> TryAcquireLeaseWithInMemoryCacheAsync(int permitCount, CancellationToken? cancellationToken=default)
		{
			try
			{
				// Use a lock to ensure thread safety when accessing the in-memory cache.
				lock (_cacheLock)
				{
					// Step 1: Try to get the existing rate limit data from the cache.
					if (!_memoryCache.TryGetValue(_partitionKey, out RateLimitData? data))
					{
						// If the key doesn't exist, initialize it.
						data = new RateLimitData
						{
							Count = 0,
							Expiry = DateTime.UtcNow.Add(_options.Window)
						};
						_memoryCache.Set(_partitionKey, data, data.Expiry);
					}

					// Step 2: If the window has expired, reset the count and expiry.
					if (data?.Expiry <= DateTime.UtcNow)
					{
						data = new RateLimitData
						{
							Count = 0,
							Expiry = DateTime.UtcNow.Add(_options.Window)
						};
						_memoryCache.Set(_partitionKey, data, data.Expiry);
					}

					// Step 3: Increment the count.
					int newCount = data!.Count + permitCount;
					data.Count = newCount;

					// Step 4: Check if the new count exceeds the limit.
					if (newCount > _options.PermitLimit)
					{
						// Not allowed. Calculate the retry-after value.
						TimeSpan retryAfter = data.Expiry - DateTime.UtcNow;
						return Task.FromResult(CreateResponse(allowed: false, count: newCount, retryAfter: retryAfter, expiry: data.Expiry));
					}

					// Step 5: Success – return the updated count.
					return Task.FromResult(CreateResponse(allowed: true, count: newCount, retryAfter: null, expiry: data.Expiry));
				}
			}
			catch (Exception ex)
			{
				// Log the error and return a successful response (fail-open behavior).
				Console.Error.WriteLine($"In-memory cache error: {ex.Message}. Allowing request (fail-open).");
				return Task.FromResult(CreateResponse(allowed: true, count: 0, retryAfter: null, expiry: DateTime.UtcNow));
			}
		}


		public void ClearInMemoryCache()
		{
			_memoryCache.Remove($"RateLimit:{_partitionKey}");
		}

		/// <summary>
		/// Lease context that carries metadata for the lease.
		/// </summary>
		private sealed class FixedWindowLeaseContext
		{
			public long Count { get; set; }
			public long Limit { get; set; }
			public TimeSpan Window { get; set; }
			public TimeSpan? RetryAfter { get; set; }
			public long? ExpiresAt { get; set; }
		}


		private sealed class FixedWindowLease : RateLimitLease
		{
			private static readonly string[] s_allMetadataNames = new[] { RateLimitMetadataName.Limit.Name, RateLimitMetadataName.Remaining.Name, RateLimitMetadataName.RetryAfter.Name };

			private readonly FixedWindowLeaseContext? _context;

			public FixedWindowLease(bool isAcquired, FixedWindowLeaseContext? context)
			{
				IsAcquired = isAcquired;
				_context = context;
			}

			public override bool IsAcquired { get; }




			public override IEnumerable<string> MetadataNames => s_allMetadataNames;

			public override bool TryGetMetadata(string metadataName, out object? metadata)
			{
				if (metadataName == RateLimitMetadataName.Limit.Name && _context is not null)
				{
					metadata = _context.Limit.ToString();
					return true;
				}

				if (metadataName == RateLimitMetadataName.Remaining.Name && _context is not null)
				{
					metadata = Math.Max(_context.Limit - _context.Count, 0);
					return true;
				}

				if (metadataName == RateLimitMetadataName.RetryAfter.Name && _context?.RetryAfter is not null)
				{
					metadata = (int)_context.RetryAfter.Value.TotalSeconds;
					return true;
				}

				if (metadataName == RateLimitMetadataName.Reset.Name && _context?.ExpiresAt is not null)
				{
					metadata = _context.ExpiresAt.Value;
					return true;
				}

				metadata = default;
				return false;
			}
		}


		/// <summary>
		/// The data stored in Memcached for each rate limiting partition.
		/// </summary>
		public class RateLimitData
		{
			public int Count { get; set; }
			public DateTime Expiry { get; set; }
		}

		/// <summary>
		/// Response from an attempt to update the rate limiter data.
		/// </summary>
		public class MemcachedFixedWindowResponse
		{
			public bool Allowed { get; set; }
			public int Count { get; set; }
			public TimeSpan? RetryAfter { get; set; }
			public long ExpiresAt { get; set; }
		}

	}


}


