using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace BuildingBlocks.Mediator.Analyzers.Tests;

internal static class AnalyzerTestHelper
{
	/// <summary>
	/// Unconstrained mediator-shaped contracts so BBM001 scenarios can compile under Roslyn.
	/// Production interfaces keep real generic constraints; analyzers match by type name.
	/// </summary>
	internal const string StubContracts = """
		namespace BuildingBlocks.Mediator
		{
			public interface ICommand { }
			public interface ICommand<out TResponse> { }
			public interface IQuery<out TResponse> { }
			public interface ICommandHandler<in TCommand> { }
			public interface ICommandHandler<in TCommand, TResponse> { }
			public interface IQueryHandler<in TQuery, TResponse> { }
		}
		""";

	internal static async Task VerifyAsync<TAnalyzer>(
		string source,
		params DiagnosticResult[] expected)
		where TAnalyzer : DiagnosticAnalyzer, new()
	{
		var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
		{
			TestCode = source,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
		};

		// Separate compilation unit so test sources may use `using BuildingBlocks.Mediator`.
		test.TestState.Sources.Add(("MediatorContracts.cs", StubContracts));
		test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	internal static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
		=> new DiagnosticResult(descriptor);
}
