using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Idempotency.AspNetCore;

/// <summary>
/// Minimal API <see cref="IEndpointFilter"/> that applies the same <see cref="IdempotencyGate"/> semantics
/// as <see cref="IdempotentAttributeFilter"/>.
/// </summary>
/// <seealso cref="IdempotencyEndpointRouteBuilderExtensions.WithIdempotency"/>
public sealed class IdempotentEndpointFilter : IEndpointFilter
{
	private readonly IdempotencyGate _gate;
	private readonly IdempotencyOptions _options;
	private readonly IdempotencyTelemetry? _telemetry;
	private readonly IdempotencyEndpointSettings _endpoint;

	/// <summary>Creates the filter.</summary>
	public IdempotentEndpointFilter(
		IDistributedCache distributedCache,
		ILoggerFactory loggerFactory,
		IdempotencyOptions options,
		IdempotencyEndpointSettings endpoint,
		IIdempotencyLock? idempotencyLock = null,
		IdempotencyTelemetry? telemetry = null)
	{
		ArgumentNullException.ThrowIfNull(distributedCache);
		ArgumentNullException.ThrowIfNull(loggerFactory);
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
		_telemetry = telemetry;
		_gate = new IdempotencyGate(
			distributedCache,
			loggerFactory.CreateLogger<IdempotentEndpointFilter>(),
			_options,
			idempotencyLock);
	}

	/// <inheritdoc />
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		var httpContext = context.HttpContext;

		if (_options.EnableRequestFingerprint)
			httpContext.Request.EnableBuffering();

		var decision = await _gate
			.BeginAsync(httpContext, _endpoint, httpContext.RequestAborted)
			.ConfigureAwait(false);

		if (!decision.ShouldExecute)
		{
			_telemetry?.RecordDecision(decision, _endpoint.UseLock, _options);

			if (decision.Kind == IdempotencyDecisionKind.Replay && decision.Envelope is { } envelope)
			{
				httpContext.Response.Headers.Append(_options.CachedResponseHeader, "true");
				return Results.Content(envelope.Body, envelope.ContentType, statusCode: envelope.StatusCode);
			}

			return ToProblemResult(decision);
		}

		var cacheKey = decision.CacheKey!;
		var requestFingerprint = decision.RequestFingerprint;

		try
		{
			var result = await next(context).ConfigureAwait(false);

			if (TryCaptureSuccessfulResult(result, out var statusCode, out var contentType, out var body))
			{
				var envelope = new IdempotencyEnvelope(statusCode, contentType, body);
				await _gate
					.CompleteAsync(cacheKey, envelope, requestFingerprint, _endpoint, httpContext.RequestAborted)
					.ConfigureAwait(false);
				_telemetry?.Record(IdempotencyOutcomes.Executed, _endpoint.UseLock, cacheKey, statusCode);
			}
			else
			{
				await _gate.AbandonAsync(cacheKey, httpContext.RequestAborted).ConfigureAwait(false);
			}

			return result;
		}
		catch
		{
			await _gate.AbandonAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
			throw;
		}
	}

	internal static bool TryCaptureSuccessfulResult(
		object? result,
		out int statusCode,
		out string contentType,
		out string body)
	{
		statusCode = StatusCodes.Status200OK;
		contentType = "application/json";
		body = string.Empty;

		if (result is null)
			return false;

		if (result is IStatusCodeHttpResult statusResult)
		{
			var code = statusResult.StatusCode ?? StatusCodes.Status200OK;
			if (code is < 200 or >= 300)
				return false;

			statusCode = code;

			if (result is IValueHttpResult { Value: { } value })
			{
				body = JsonSerializer.Serialize(value);
				contentType = "application/json";
				return true;
			}

			if (result is IContentTypeHttpResult { ContentType: { } ct } && !string.IsNullOrEmpty(ct))
				contentType = ct;

			if (result is ContentHttpResult content)
			{
				body = content.ResponseContent ?? string.Empty;
				contentType = string.IsNullOrEmpty(content.ContentType) ? "application/json" : content.ContentType!;
				return true;
			}

			// Empty 2xx (e.g. 204 No Content, Results.Ok() without value)
			body = string.Empty;
			return true;
		}

		if (result is ProblemDetails)
			return false;

		// Untyped object return from handler — treat as 200 JSON
		body = JsonSerializer.Serialize(result);
		contentType = "application/json";
		statusCode = StatusCodes.Status200OK;
		return true;
	}

	private IResult ToProblemResult(IdempotencyDecision decision)
	{
		var problem = IdempotencyProblemDetails.For(decision, _options);
		return Results.Problem(
			detail: problem.Detail,
			statusCode: problem.Status,
			title: problem.Title,
			type: problem.Type,
			extensions: problem.Extensions);
	}
}

/// <summary>Minimal API registration helpers for BuildingBlocks.Idempotency.</summary>
public static class IdempotencyEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Adds <see cref="IdempotentEndpointFilter"/> to the endpoint.
	/// </summary>
	/// <param name="builder">Route handler builder.</param>
	/// <param name="useLock">When true, GetOrCreate uses <see cref="IIdempotencyLock"/>.</param>
	/// <param name="processingTtlSeconds">Optional Processing TTL override; <c>0</c> = global.</param>
	/// <param name="entryTtlSeconds">Optional Completed TTL override; <c>0</c> = global.</param>
	/// <example>
	/// <code>
	/// app.MapPost("/orders", CreateAsync).WithIdempotency(useLock: true);
	/// </code>
	/// </example>
	/// <seealso cref="IdempotentAttribute"/>
	public static RouteHandlerBuilder WithIdempotency(
		this RouteHandlerBuilder builder,
		bool useLock = false,
		int processingTtlSeconds = 0,
		int entryTtlSeconds = 0)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var endpoint = IdempotencyEndpointSettings.Create(useLock, processingTtlSeconds, entryTtlSeconds);

		return builder.AddEndpointFilter(async (context, next) =>
		{
			var sp = context.HttpContext.RequestServices;
			var filter = new IdempotentEndpointFilter(
				sp.GetRequiredService<IDistributedCache>(),
				sp.GetRequiredService<ILoggerFactory>(),
				sp.GetService<IOptions<IdempotencyOptions>>()?.Value ?? new IdempotencyOptions(),
				endpoint,
				sp.GetService<IIdempotencyLock>(),
				sp.GetService<IdempotencyTelemetry>());

			return await filter.InvokeAsync(context, next).ConfigureAwait(false);
		});
	}
}
