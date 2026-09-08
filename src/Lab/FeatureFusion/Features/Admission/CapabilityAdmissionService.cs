using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FeatureFusion.Features.Orders.Commands;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeatureFusion.Features.Admission;

/// <summary>EF-backed admission gate (lab proof — not a NuGet package).</summary>
public sealed class CapabilityAdmissionService : ICapabilityAdmission
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	private readonly CatalogDbContext _db;
	private readonly CapabilityAdmissionOptions _options;
	private readonly TimeProvider _time;
	private readonly IReadOnlyDictionary<string, ICapabilityExecutor> _executors;

	public CapabilityAdmissionService(
		CatalogDbContext db,
		IOptions<CapabilityAdmissionOptions> options,
		IEnumerable<ICapabilityExecutor> executors,
		TimeProvider? time = null)
	{
		_db = db;
		_options = options.Value;
		_time = time ?? TimeProvider.System;
		_executors = executors.ToDictionary(e => e.CapabilityId, StringComparer.Ordinal);
	}

	public async Task<AdmissionDecision> AdmitAsync(
		string capabilityId,
		string requestKey,
		string intentPayloadJson,
		string intentHash,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
		ArgumentException.ThrowIfNullOrWhiteSpace(intentPayloadJson);
		ArgumentException.ThrowIfNullOrWhiteSpace(intentHash);

		if (!_options.IsDeferred(capabilityId))
			return new AdmissionDecision.Allow();

		if (string.IsNullOrWhiteSpace(requestKey))
		{
			return new AdmissionDecision.Deny(
				$"Capability '{capabilityId}' requires a request key to defer (HTTP Idempotency-Key or MCP idempotencyKey).",
				StatusCodes.Status400BadRequest);
		}

		var now = _time.GetUtcNow();
		var existing = await _db.IntentTickets
			.AsNoTracking()
			.FirstOrDefaultAsync(
				t => t.CapabilityId == capabilityId && t.RequestKey == requestKey,
				cancellationToken)
			.ConfigureAwait(false);

		if (existing is not null)
		{
			if (existing.Status == IntentTicketStatus.Pending && existing.ExpiresAt > now)
			{
				if (!string.Equals(existing.IntentHash, intentHash, StringComparison.Ordinal))
				{
					return new AdmissionDecision.Deny(
						"Request key was reused with a different intent payload while the ticket is Pending.",
						StatusCodes.Status422UnprocessableEntity);
				}

				return new AdmissionDecision.Defer(ToPending(existing));
			}

			if (existing.Status == IntentTicketStatus.Released)
			{
				return new AdmissionDecision.Deny(
					$"Ticket '{existing.Id}' was already released for this request key.",
					StatusCodes.Status409Conflict);
			}

			await _db.IntentTickets
				.Where(t => t.Id == existing.Id)
				.ExecuteDeleteAsync(cancellationToken)
				.ConfigureAwait(false);
		}

		var ticket = new IntentTicket
		{
			Id = Guid.NewGuid(),
			CapabilityId = capabilityId,
			RequestKey = requestKey,
			IntentHash = intentHash,
			IntentPayload = intentPayloadJson,
			Status = IntentTicketStatus.Pending,
			CreatedAt = now,
			ExpiresAt = now + _options.TicketTtl
		};

		_db.IntentTickets.Add(ticket);
		try
		{
			await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateException)
		{
			var winner = await _db.IntentTickets
				.AsNoTracking()
				.FirstAsync(
					t => t.CapabilityId == capabilityId && t.RequestKey == requestKey,
					cancellationToken)
				.ConfigureAwait(false);

			if (winner.Status == IntentTicketStatus.Pending
			    && !string.Equals(winner.IntentHash, intentHash, StringComparison.Ordinal))
			{
				return new AdmissionDecision.Deny(
					"Request key was reused with a different intent payload while the ticket is Pending.",
					StatusCodes.Status422UnprocessableEntity);
			}

			if (winner.Status == IntentTicketStatus.Pending)
				return new AdmissionDecision.Defer(ToPending(winner));

			return new AdmissionDecision.Deny(
				$"Ticket '{winner.Id}' is not Pending for this request key.",
				StatusCodes.Status409Conflict);
		}

		return new AdmissionDecision.Defer(ToPending(ticket));
	}

	public async Task<AdmissionReleaseResult> ReleaseAsync(
		Guid ticketId,
		string? releasedBy,
		CancellationToken cancellationToken)
	{
		var now = _time.GetUtcNow();
		var by = string.IsNullOrWhiteSpace(releasedBy) ? "human" : releasedBy.Trim();

		var claimed = await _db.IntentTickets
			.Where(t => t.Id == ticketId
			            && t.Status == IntentTicketStatus.Pending
			            && t.ExpiresAt > now)
			.ExecuteUpdateAsync(
				s => s
					.SetProperty(t => t.Status, IntentTicketStatus.Released)
					.SetProperty(t => t.ReleasedAt, now)
					.SetProperty(t => t.ReleasedBy, by),
				cancellationToken)
			.ConfigureAwait(false);

		if (claimed != 1)
		{
			var current = await _db.IntentTickets
				.AsNoTracking()
				.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
				.ConfigureAwait(false);

			if (current is null)
			{
				return new AdmissionReleaseResult(
					false, $"Ticket '{ticketId}' was not found.", StatusCodes.Status404NotFound, null, ticketId);
			}

			if (current.Status == IntentTicketStatus.Pending && current.ExpiresAt <= now)
			{
				await _db.IntentTickets
					.Where(t => t.Id == ticketId && t.Status == IntentTicketStatus.Pending)
					.ExecuteUpdateAsync(
						s => s.SetProperty(t => t.Status, IntentTicketStatus.Expired),
						cancellationToken)
					.ConfigureAwait(false);

				return new AdmissionReleaseResult(
					false, $"Ticket '{ticketId}' has expired.", StatusCodes.Status410Gone, null, ticketId);
			}

			if (current.Status == IntentTicketStatus.Released)
			{
				return new AdmissionReleaseResult(
					false,
					$"Ticket '{ticketId}' was already released.",
					StatusCodes.Status409Conflict,
					current.ExecutionOrderId,
					ticketId);
			}

			return new AdmissionReleaseResult(
				false, $"Ticket '{ticketId}' cannot be released (status={current.Status}).",
				StatusCodes.Status409Conflict, null, ticketId);
		}

		var ticket = await _db.IntentTickets
			.AsNoTracking()
			.FirstAsync(t => t.Id == ticketId, cancellationToken)
			.ConfigureAwait(false);

		if (!_executors.TryGetValue(ticket.CapabilityId, out var executor))
		{
			return new AdmissionReleaseResult(
				false, $"Unsupported capability '{ticket.CapabilityId}'.", StatusCodes.Status400BadRequest, null, ticketId);
		}

		// Failure boundary: ticket is already Released before Execute.
		// Concurrent releases cannot both claim the ticket; a crash after claim may leave Released without an order.
		var outcome = await executor.ExecuteAsync(ticket.IntentPayload, cancellationToken).ConfigureAwait(false);
		if (!outcome.Success)
		{
			return new AdmissionReleaseResult(
				false, outcome.Error, outcome.StatusCode, null, ticketId);
		}

		await _db.IntentTickets
			.Where(t => t.Id == ticketId)
			.ExecuteUpdateAsync(
				s => s.SetProperty(t => t.ExecutionOrderId, outcome.ExecutionId),
				cancellationToken)
			.ConfigureAwait(false);

		return new AdmissionReleaseResult(true, null, StatusCodes.Status200OK, outcome.ExecutionId, ticketId);
	}

	/// <summary>Stable intent fingerprint for <see cref="CreateOrderCommand"/> (orders.create).</summary>
	public static string HashCreateOrderIntent(CreateOrderCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);
		string canonical;
		if (command.Items is null || command.Items.Count == 0)
		{
			// Exp / HTTP flat-body contract.
			canonical = $"{command.ProductId}|{command.Quantity}|{command.CustomerId}";
		}
		else
		{
			var lines = command.Items
				.OrderBy(l => l.ProductId)
				.ThenBy(l => l.Quantity)
				.Select(l => $"{l.ProductId}x{l.Quantity}");
			canonical = $"{command.CustomerId}|{string.Join(',', lines)}";
		}

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
		return Convert.ToHexString(hash);
	}

	public static string SerializeCreateOrderIntent(CreateOrderCommand command)
		=> JsonSerializer.Serialize(command, JsonOptions);

	private static AdmissionPendingResponse ToPending(IntentTicket ticket) =>
		new(
			ticket.Id,
			ticket.CapabilityId,
			nameof(IntentTicketStatus.Pending),
			ticket.CreatedAt,
			ticket.ExpiresAt);
}
