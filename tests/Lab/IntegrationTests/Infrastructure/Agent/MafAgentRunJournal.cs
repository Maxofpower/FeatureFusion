using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace IntegrationTests.Infrastructure.Agent;

/// <summary>
/// Extracts ordered tool-call evidence from a MAF <see cref="AgentResponse"/>.
/// </summary>
public static class MafAgentRunJournal
{
	public static IReadOnlyList<MafObservedToolCall> ReadToolCalls(AgentResponse response)
	{
		var calls = new List<MafObservedToolCall>();
		var ordinal = 0;

		foreach (var message in response.Messages)
		{
			foreach (var content in message.Contents)
			{
				switch (content)
				{
					case FunctionCallContent call:
						calls.Add(new MafObservedToolCall(
							Ordinal: ++ordinal,
							Kind: "call",
							ToolName: call.Name,
							ArgumentsJson: SerializeArguments(call.Arguments),
							ResultJson: null,
							Error: null));
						break;
					case FunctionResultContent result:
						if (calls.Count == 0 || calls[^1].Kind != "call")
						{
							calls.Add(new MafObservedToolCall(
								Ordinal: ++ordinal,
								Kind: "result",
								ToolName: result.CallId,
								ArgumentsJson: null,
								ResultJson: SerializeResult(result.Result),
								Error: null));
							break;
						}

						var pending = calls[^1];
						calls[^1] = pending with
						{
							Kind = "call+result",
							ResultJson = SerializeResult(result.Result),
							Error = TryReadError(result.Result)
						};
						break;
				}
			}
		}

		return calls;
	}

	public static string DescribeSequence(IEnumerable<MafObservedToolCall> calls)
		=> string.Join(" → ", calls.Select(c => c.ToolName));

	private static string? SerializeArguments(IDictionary<string, object?>? arguments)
	{
		if (arguments is null || arguments.Count == 0)
			return "{}";

		return JsonSerializer.Serialize(arguments);
	}

	private static string? SerializeResult(object? result)
	{
		if (result is null)
			return null;

		return result switch
		{
			string text => text,
			_ => JsonSerializer.Serialize(result)
		};
	}

	private static string? TryReadError(object? result)
	{
		if (result is null)
			return null;

		var text = result switch
		{
			string s => s,
			_ => JsonSerializer.Serialize(result)
		};

		if (text.Contains("error", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("ConfirmationRequired", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("IdempotencyKeyRequired", StringComparison.OrdinalIgnoreCase))
		{
			return text.Length <= 500 ? text : text[..500];
		}

		return null;
	}
}

public sealed record MafObservedToolCall(
	int Ordinal,
	string Kind,
	string ToolName,
	string? ArgumentsJson,
	string? ResultJson,
	string? Error);
