using BuildingBlocks.Mediator;
using FeatureFusion.Features.Orders.Commands;
using System.Text.Json;

namespace FeatureFusion.Features.Admission;

/// <summary>
/// Capability-oriented execution after an IntentTicket is claimed (Released).
/// Admission core does not know CreateOrder — hosts register one executor per capability id.
/// </summary>
public interface ICapabilityExecutor
{
	string CapabilityId { get; }

	Task<CapabilityExecutionOutcome> ExecuteAsync(string intentPayloadJson, CancellationToken cancellationToken);
}

/// <summary>Result of executing a deferred capability intent.</summary>
public sealed record CapabilityExecutionOutcome(
	bool Success,
	string? Error,
	int StatusCode,
	Guid? ExecutionId);

/// <summary>Executes deferred <see cref="CapabilityIds.OrdersCreate"/> via Mediator (same command as HTTP/MCP).</summary>
public sealed class CreateOrderCapabilityExecutor : ICapabilityExecutor
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	private readonly ISender _sender;

	public CreateOrderCapabilityExecutor(ISender sender) => _sender = sender;

	public string CapabilityId => CapabilityIds.OrdersCreate;

	public async Task<CapabilityExecutionOutcome> ExecuteAsync(
		string intentPayloadJson,
		CancellationToken cancellationToken)
	{
		CreateOrderCommand command;
		try
		{
			command = JsonSerializer.Deserialize<CreateOrderCommand>(intentPayloadJson, JsonOptions)
				?? throw new JsonException("Intent payload deserialized to null.");
		}
		catch (JsonException ex)
		{
			return new CapabilityExecutionOutcome(
				false, $"Invalid intent payload: {ex.Message}", StatusCodes.Status500InternalServerError, null);
		}

		var result = await _sender.Send(command, cancellationToken).ConfigureAwait(false);
		if (!result.IsSuccess)
		{
			return new CapabilityExecutionOutcome(
				false,
				result.Error,
				result.StatusCode == 0 ? StatusCodes.Status500InternalServerError : result.StatusCode,
				null);
		}

		return new CapabilityExecutionOutcome(true, null, StatusCodes.Status200OK, result.Value.OrderId);
	}
}
