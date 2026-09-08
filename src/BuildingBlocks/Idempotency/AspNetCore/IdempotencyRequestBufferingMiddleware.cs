using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Idempotency.AspNetCore;

/// <summary>
/// Enables request body buffering early for requests that carry an
/// <c>Idempotency-Key</c> so Minimal API <see cref="IdempotentEndpointFilter"/> can fingerprint
/// the body after model binding has already read it.
/// </summary>
/// <remarks>
/// MVC <see cref="IdempotentAttribute"/> uses a resource filter that buffers before binding.
/// Minimal API endpoint filters run after binding; without early buffering the body stream is
/// empty when <see cref="IdempotencyGate"/> hashes it and fingerprint conflicts never fire.
/// </remarks>
public static class IdempotencyApplicationBuilderExtensions
{
	/// <summary>
	/// Buffers the request body when the configured idempotency header is present.
	/// Call before endpoint routing (typically early in the pipeline, after exception handling).
	/// </summary>
	public static IApplicationBuilder UseIdempotencyRequestBuffering(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);

		return app.Use(async (context, next) =>
		{
			var options = context.RequestServices.GetService<IOptionsMonitor<IdempotencyOptions>>()?.CurrentValue
				?? context.RequestServices.GetService<IOptions<IdempotencyOptions>>()?.Value;

			if (options is not null
				&& context.Request.Headers.ContainsKey(options.HeaderName))
			{
				context.Request.EnableBuffering();
			}

			await next().ConfigureAwait(false);
		});
	}
}
