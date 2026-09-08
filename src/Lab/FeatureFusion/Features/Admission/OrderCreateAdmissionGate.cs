using BuildingBlocks.Mcp;
using FeatureFusion.Features.Orders.Commands;
using Microsoft.AspNetCore.Http;

namespace FeatureFusion.Features.Admission;

/// <summary>
/// Shared pre-<c>ISender.Send</c> gate for <see cref="CapabilityIds.OrdersCreate"/>.
/// Used by HTTP Order endpoints and MCP <c>UseDispatcher</c>.
/// </summary>
public static class OrderCreateAdmissionGate
{
	public static async Task<AdmissionDecision> AdmitCreateOrderAsync(
		ICapabilityAdmission admission,
		CreateOrderCommand command,
		string? requestKey,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(admission);
		ArgumentNullException.ThrowIfNull(command);

		var payload = CapabilityAdmissionService.SerializeCreateOrderIntent(command);
		var hash = CapabilityAdmissionService.HashCreateOrderIntent(command);
		return await admission.AdmitAsync(
				CapabilityIds.OrdersCreate,
				requestKey ?? "",
				payload,
				hash,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public static string? ResolveHttpRequestKey(HttpRequest request)
	{
		if (request.Headers.TryGetValue("Idempotency-Key", out var values))
		{
			var key = values.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(key))
				return key;
		}

		return null;
	}

	public static string? ResolveMcpRequestKey(IMcpInvokeContextAccessor? accessor)
		=> accessor?.Current?.IdempotencyKey;
}
