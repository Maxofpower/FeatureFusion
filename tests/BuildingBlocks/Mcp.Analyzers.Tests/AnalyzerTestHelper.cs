using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace BuildingBlocks.Mcp.Analyzers.Tests;

internal static class AnalyzerTestHelper
{
	internal const string StubAttribute = """
		using System;
		namespace BuildingBlocks.Mcp
		{
			public enum McpToolKind { Unspecified = 0, Query = 1, Command = 2 }

			[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
			public sealed class McpToolAttribute : Attribute
			{
				public McpToolAttribute(string name) => Name = name;
				public string Name { get; }
				public string Description { get; set; } = "";
				public McpToolKind Kind { get; set; }
				public bool Idempotent { get; set; }
			}
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
		test.TestState.Sources.Add(("McpToolAttribute.cs", StubAttribute));
		test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	internal static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
		=> new DiagnosticResult(descriptor);
}
