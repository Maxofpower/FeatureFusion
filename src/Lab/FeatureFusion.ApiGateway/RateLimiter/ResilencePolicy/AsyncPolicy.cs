//using Polly;
//using Polly.CircuitBreaker;
//using Polly.Retry;


//namespace FeatureFusion.ApiGateway.RateLimiter.ResilencePolicy
//{
	

//	// Define the retry policy with exponential backoff.
//	var retryPolicy = Policy
//		.Handle<Exception>() // Handle all exceptions.
//		.WaitAndRetryAsync(
//			retryCount: 3, // Retry 3 times.
//			sleepDurationProvider: retryAttempt =>
//			{
//				// Exponential backoff: 1 minute, 5 minutes, 10 minutes.
//				return TimeSpan.FromMinutes(Math.Pow(2, retryAttempt - 1));
//			},
//			onRetry: (exception, delay, retryCount, context) =>
//			{
//				// Log the retry attempt.
//				Console.WriteLine($"Retry {retryCount}: Waiting {delay} before next retry. Error: {exception.Message}");
//			}
//		);

//	// Define the circuit breaker policy.
//	var circuitBreakerPolicy = Policy
//		.Handle<Exception>() // Handle all exceptions.
//		.CircuitBreakerAsync(
//			exceptionsAllowedBeforeBreaking: 3, // Trip after 3 failures.
//			durationOfBreak: TimeSpan.FromMinutes(10) // Break for 10 minutes.
//		);

//	// Combine the retry policy with the circuit breaker policy.
//	var resiliencePolicy = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
//}
