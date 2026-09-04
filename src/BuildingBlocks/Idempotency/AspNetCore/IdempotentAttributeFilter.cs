using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Idempotency.AspNetCore;

/// <summary>
/// Resource + action filter that implements <c>Idempotency-Key</c> handling via
/// <see cref="IdempotencyGate"/> and optional <see cref="IIdempotencyLock"/>.
/// </summary>
/// <seealso cref="IdempotentAttribute"/>
/// <seealso cref="IdempotentEndpointFilter"/>
public sealed class IdempotentAttributeFilter : IAsyncResourceFilter, IAsyncActionFilter
{
	private readonly IdempotencyGate _gate;
	private readonly IdempotencyOptions _options;
	private readonly IdempotencyTelemetry? _telemetry;
	private readonly IdempotencyEndpointSettings _endpoint;

	/// <summary>Creates the filter.</summary>
	public IdempotentAttributeFilter(
		IDistributedCache distributedCache,
		ILoggerFactory loggerFactory,
		IIdempotencyLock? idempotencyLock,
		IdempotencyOptions options,
		bool useLock,
		IdempotencyTelemetry? telemetry = null,
		int processingTtlSeconds = 0,
		int entryTtlSeconds = 0)
	{
		ArgumentNullException.ThrowIfNull(distributedCache);
		ArgumentNullException.ThrowIfNull(loggerFactory);
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_telemetry = telemetry;
		_endpoint = IdempotencyEndpointSettings.Create(useLock, processingTtlSeconds, entryTtlSeconds);
		_gate = new IdempotencyGate(
			distributedCache,
			loggerFactory.CreateLogger<IdempotentAttributeFilter>(),
			_options,
			idempotencyLock);
	}

	/// <inheritdoc />
	public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
	{
		var headerName = _options.HeaderName;
		string? rawKey = null;
		if (context.HttpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
			rawKey = headerValue.ToString();

		if (!IdempotencyKeyValidator.TryValidate(rawKey, _options, out var error))
		{
			var decision = IdempotencyDecision.BadKey(error!);
			_telemetry?.RecordDecision(decision, _endpoint.UseLock, _options);
			context.Result = ToProblemResult(decision);
			return;
		}

		if (_options.EnableRequestFingerprint)
			context.HttpContext.Request.EnableBuffering();

		await next().ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		var decision = await _gate
			.BeginAsync(context.HttpContext, _endpoint, context.HttpContext.RequestAborted)
			.ConfigureAwait(false);

		if (!decision.ShouldExecute)
		{
			_telemetry?.RecordDecision(decision, _endpoint.UseLock, _options);

			if (decision.Kind == IdempotencyDecisionKind.Replay && decision.Envelope is { } envelope)
			{
				context.Result = new ContentResult
				{
					Content = envelope.Body,
					ContentType = envelope.ContentType,
					StatusCode = envelope.StatusCode
				};
				context.HttpContext.Response.Headers.Append(_options.CachedResponseHeader, "true");
				return;
			}

			context.Result = ToProblemResult(decision);
			return;
		}

		var cacheKey = decision.CacheKey!;
		var requestFingerprint = decision.RequestFingerprint;

		try
		{
			var executedContext = await next().ConfigureAwait(false);

			if (TryCaptureSuccessfulResponse(executedContext.Result, out var statusCode, out var contentType, out var body))
			{
				var envelope = new IdempotencyEnvelope(statusCode, contentType, body);
				await _gate
					.CompleteAsync(cacheKey, envelope, requestFingerprint, _endpoint, context.HttpContext.RequestAborted)
					.ConfigureAwait(false);
				_telemetry?.Record(IdempotencyOutcomes.Executed, _endpoint.UseLock, cacheKey, statusCode);
			}
			else
			{
				await _gate.AbandonAsync(cacheKey, context.HttpContext.RequestAborted).ConfigureAwait(false);
			}
		}
		catch (Exception)
		{
			await _gate.AbandonAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>
	/// Captures a successful action result for cache storage.
	/// Serializes <see cref="ObjectResult"/> values with <see cref="JsonSerializer"/> (System.Text.Json).
	/// Caches all HTTP 2xx results (including null StatusCode meaning 200).
	/// </summary>
	internal static bool TryCaptureSuccessfulResponse(
		IActionResult? result,
		out int statusCode,
		out string contentType,
		out string body)
	{
		statusCode = StatusCodes.Status200OK;
		contentType = "application/json";
		body = string.Empty;

		if (result is ObjectResult obj && IsSuccessStatusCode(obj.StatusCode))
		{
			statusCode = obj.StatusCode ?? StatusCodes.Status200OK;
			contentType = "application/json";
			body = JsonSerializer.Serialize(obj.Value);
			return true;
		}

		if (result is ContentResult content && IsSuccessStatusCode(content.StatusCode))
		{
			statusCode = content.StatusCode ?? StatusCodes.Status200OK;
			contentType = string.IsNullOrEmpty(content.ContentType) ? "application/json" : content.ContentType;
			body = content.Content ?? string.Empty;
			return true;
		}

		if (result is StatusCodeResult statusOnly && IsSuccessStatusCode(statusOnly.StatusCode))
		{
			statusCode = statusOnly.StatusCode;
			contentType = "application/json";
			body = string.Empty;
			return true;
		}

		if (result is EmptyResult)
		{
			statusCode = StatusCodes.Status200OK;
			contentType = "application/json";
			body = string.Empty;
			return true;
		}

		return false;
	}

	/// <summary>Validates and returns the raw idempotency header value.</summary>
	/// <exception cref="ArgumentNullException">Header is missing.</exception>
	/// <exception cref="ArgumentException">Header is empty, invalid, or not a ULID when required.</exception>
	public string ExtractAndValidateIdempotencyKey(HttpRequest httpRequest)
	{
		ArgumentNullException.ThrowIfNull(httpRequest);

		var headerName = _options.HeaderName;
		string? rawKey = null;
		if (httpRequest.Headers.TryGetValue(headerName, out var value))
			rawKey = value.ToString();

		if (rawKey is null)
			throw new ArgumentNullException(headerName, $"The {headerName} header is missing.");

		if (!IdempotencyKeyValidator.TryValidate(rawKey, _options, out var error))
			throw new ArgumentException(error);

		return rawKey;
	}

	/// <summary>Validates and parses the idempotency header as a ULID when <see cref="IdempotencyOptions.RequireUlid"/> is true.</summary>
	public Ulid ExtractAndValidateIdempotencyKeyAsUlid(HttpRequest httpRequest)
	{
		var raw = ExtractAndValidateIdempotencyKey(httpRequest);
		if (!Ulid.TryParse(raw, out var ulid))
			throw new ArgumentException($"Invalid {_options.HeaderName} format: {raw}");
		return ulid;
	}

	private ObjectResult ToProblemResult(IdempotencyDecision decision)
	{
		var problem = IdempotencyProblemDetails.For(decision, _options);
		return new ObjectResult(problem)
		{
			StatusCode = problem.Status,
			ContentTypes = { "application/problem+json" }
		};
	}

	private static bool IsSuccessStatusCode(int? statusCode) =>
		statusCode is null || (statusCode >= 200 && statusCode < 300);
}
