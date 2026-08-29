using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace BuildingBlocks.Mcp.Analyzers.Tests;

public sealed class McpToolAttributeAnalyzerTests
{
	[Fact]
	public async Task BBMCP001_When_Description_Missing()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			[McpTool("orders.create")]
			public sealed class {|#0:CreateOrder|} { }
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new McpToolAttributeAnalyzer().SupportedDiagnostics[0])
			.WithLocation(0)
			.WithArguments("CreateOrder");

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source, expected);
	}

	[Fact]
	public async Task NoDiagnostic_When_Command_Omits_Idempotent()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			[McpTool("orders.create", Description = "Create", Kind = McpToolKind.Command)]
			public sealed class CreateOrder { }
			""";

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source);
	}

	[Fact]
	public async Task BBMCP003_When_Duplicate_Names()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			[McpTool("dup", Description = "A")]
			public sealed class {|#0:First|} { }

			[McpTool("dup", Description = "B")]
			public sealed class {|#1:Second|} { }
			""";

		var d = new McpToolAttributeAnalyzer().SupportedDiagnostics[2];
		var expected0 = AnalyzerTestHelper.Diagnostic(d).WithLocation(0).WithArguments("dup");
		var expected1 = AnalyzerTestHelper.Diagnostic(d).WithLocation(1).WithArguments("dup");

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source, expected0, expected1);
	}

	[Fact]
	public async Task BBMCP004_When_Attribute_On_Interface()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			[McpTool("bad", Description = "No")]
			public abstract class {|#0:BadTool|} { }
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new McpToolAttributeAnalyzer().SupportedDiagnostics[3])
			.WithLocation(0)
			.WithArguments("BadTool");

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source, expected);
	}

	[Fact]
	public async Task NoDiagnostic_When_Query_Has_Description()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			[McpTool("products.list", Description = "List", Kind = McpToolKind.Query)]
			public sealed class ListProducts { }
			""";

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source);
	}

	[Fact]
	public async Task BBMCP005_When_Attribute_On_Instance_Method()
	{
		const string source = """
			using BuildingBlocks.Mcp;

			public sealed class Host
			{
				[McpTool("bad", Description = "No")]
				public string {|#0:Run|}() => "x";
			}
			""";

		var expected = AnalyzerTestHelper.Diagnostic(new McpToolAttributeAnalyzer().SupportedDiagnostics[4])
			.WithLocation(0)
			.WithArguments("Run");

		await AnalyzerTestHelper.VerifyAsync<McpToolAttributeAnalyzer>(source, expected);
	}
}
