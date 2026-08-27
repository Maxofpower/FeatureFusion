using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace BuildingBlocks.Mediator.Analyzers.Tests;

public sealed class WrongHandlerKindAnalyzerTests
{
	[Fact]
	public async Task BBM001_When_CommandHandler_Targets_Query()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class SampleQuery : IQuery<int> { }

			public sealed class {|#0:BadHandler|} : ICommandHandler<SampleQuery, int>
			{
			}
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new WrongHandlerKindAnalyzer().SupportedDiagnostics[0])
			.WithLocation(0)
			.WithArguments("BadHandler", "ICommandHandler<SampleQuery, int>", "SampleQuery");

		await AnalyzerTestHelper.VerifyAsync<WrongHandlerKindAnalyzer>(source, expected);
	}

	[Fact]
	public async Task BBM001_When_QueryHandler_Targets_Command()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class SampleCommand : ICommand<int> { }

			public sealed class {|#0:BadHandler|} : IQueryHandler<SampleCommand, int>
			{
			}
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new WrongHandlerKindAnalyzer().SupportedDiagnostics[0])
			.WithLocation(0)
			.WithArguments("BadHandler", "IQueryHandler<SampleCommand, int>", "SampleCommand");

		await AnalyzerTestHelper.VerifyAsync<WrongHandlerKindAnalyzer>(source, expected);
	}

	[Fact]
	public async Task NoDiagnostic_When_CommandHandler_Targets_Command()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class SampleCommand : ICommand<int> { }

			public sealed class GoodHandler : ICommandHandler<SampleCommand, int>
			{
			}
			""";

		await AnalyzerTestHelper.VerifyAsync<WrongHandlerKindAnalyzer>(source);
	}
}
