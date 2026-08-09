using Xunit;

namespace BuildingBlocks.Mediator.Analyzers.Tests;

public sealed class MissingHandlerInCompilationAnalyzerTests
{
	[Fact]
	public async Task BBM002_When_Command_Has_No_Handler()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class {|#0:OrphanCommand|} : ICommand<int> { }
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new MissingHandlerInCompilationAnalyzer().SupportedDiagnostics[0])
			.WithLocation(0)
			.WithArguments("OrphanCommand");

		await AnalyzerTestHelper.VerifyAsync<MissingHandlerInCompilationAnalyzer>(source, expected);
	}

	[Fact]
	public async Task NoDiagnostic_When_Command_Has_Handler()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class SampleCommand : ICommand<int> { }

			public sealed class SampleCommandHandler : ICommandHandler<SampleCommand, int>
			{
			}
			""";

		await AnalyzerTestHelper.VerifyAsync<MissingHandlerInCompilationAnalyzer>(source);
	}

	[Fact]
	public async Task NoDiagnostic_When_Query_Has_Handler()
	{
		const string source = """
			using BuildingBlocks.Mediator;

			public sealed class SampleQuery : IQuery<int> { }

			public sealed class SampleQueryHandler : IQueryHandler<SampleQuery, int>
			{
			}
			""";

		await AnalyzerTestHelper.VerifyAsync<MissingHandlerInCompilationAnalyzer>(source);
	}

	[Fact]
	public async Task NoDiagnostic_When_Closed_Message_Satisfied_By_OpenGeneric_Handler()
	{
		const string source = """
			using BuildingBlocks.Mediator;
			using System.Threading;
			using System.Threading.Tasks;

			public sealed class FixedPing : ICommand<string> { }

			public sealed class OpenFixedPingHandler<TDep> : ICommandHandler<FixedPing, string>
			{
				public Task<string> Handle(FixedPing command, CancellationToken cancellationToken)
					=> Task.FromResult("pong");
			}
			""";

		await AnalyzerTestHelper.VerifyAsync<MissingHandlerInCompilationAnalyzer>(source);
	}
}
