using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FluentValidation;

namespace FeatureFusion.Features.MediatorDemo.Commands;

/// <summary>
/// Infra-free command for Swagger / Aspire Mediator smoke tests.
/// Validated by <see cref="EchoCommandValidator"/> through host <c>ValidationBehavior</c>.
/// </summary>
[McpTool("demo.echo", Description = "Echo a message (lab smoke tool)", Idempotent = false)]
public sealed class EchoCommand : ICommand<Result<EchoResponse>>
{
	/// <summary>
	/// When <see cref="Message"/> equals this value (after trim), the handler throws
	/// so integration tests / Aspire can observe unhandled pipeline faults (HTTP 500).
	/// </summary>
	public const string FaultTrigger = "__throw__";

	[System.ComponentModel.Description("Text to echo back")]
	public string Message { get; set; } = default!;
}

public sealed record EchoResponse(string Echo, DateTimeOffset TimestampUtc);

/// <summary>
/// FluentValidation for <see cref="EchoCommand"/> — registered via
/// <c>AddFluentValidatorsFromAssemblies</c> and enforced by <c>ValidationBehavior</c> (pipeline order 0).
/// </summary>
public sealed class EchoCommandValidator : AbstractValidator<EchoCommand>
{
	public EchoCommandValidator()
	{
		RuleFor(x => x.Message)
			.NotEmpty().WithMessage("Message is required.")
			.MaximumLength(200).WithMessage("Message must be 200 characters or fewer.");
	}
}

public sealed class EchoCommandHandler : ICommandHandler<EchoCommand, Result<EchoResponse>>
{
	public Task<Result<EchoResponse>> Handle(EchoCommand request, CancellationToken cancellationToken)
	{
		var message = request.Message.Trim();
		if (string.Equals(message, EchoCommand.FaultTrigger, StringComparison.Ordinal))
			throw new InvalidOperationException("MediatorDemo fault trigger");

		var response = new EchoResponse(
			Echo: message,
			TimestampUtc: DateTimeOffset.UtcNow);

		return Task.FromResult(Result<EchoResponse>.Success(response));
	}
}
