using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BuildingBlocks.Mcp.Analyzers;

/// <summary>
/// BBMCP001–005 for <c>[McpTool]</c> usage.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class McpToolAttributeAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Missing Description.</summary>
	public const string MissingDescriptionId = "BBMCP001";

	/// <summary>Command without Idempotent.</summary>
	public const string MissingIdempotentId = "BBMCP002";

	/// <summary>Duplicate tool name.</summary>
	public const string DuplicateNameId = "BBMCP003";

	/// <summary>Attribute on abstract/interface type or non-static method.</summary>
	public const string InvalidTypeId = "BBMCP004";

	/// <summary>[McpTool] on an instance method.</summary>
	public const string InstanceMethodId = "BBMCP005";

	private static readonly DiagnosticDescriptor MissingDescription = new(
		MissingDescriptionId,
		title: "[McpTool] is missing Description",
		messageFormat: "Type '{0}' has [McpTool] without Description",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor MissingIdempotent = new(
		MissingIdempotentId,
		title: "Command MCP tool should set Idempotent",
		messageFormat: "Type '{0}' is an MCP command tool without Idempotent = true",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor DuplicateName = new(
		DuplicateNameId,
		title: "Duplicate [McpTool] name",
		messageFormat: "MCP tool name '{0}' is used more than once in this compilation",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		customTags: WellKnownDiagnosticTags.CompilationEnd);

	private static readonly DiagnosticDescriptor InvalidType = new(
		InvalidTypeId,
		title: "[McpTool] requires a concrete type",
		messageFormat: "Type '{0}' cannot have [McpTool] because it is abstract or an interface",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor InstanceMethod = new(
		InstanceMethodId,
		title: "[McpTool] on instance method",
		messageFormat: "Method '{0}' cannot have [McpTool] because it is not public static (Minimal API / MapGet style)",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
		=> ImmutableArray.Create(MissingDescription, MissingIdempotent, DuplicateName, InvalidType, InstanceMethod);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(start =>
		{
			var names = new Dictionary<string, List<ISymbol>>(StringComparer.OrdinalIgnoreCase);
			start.RegisterSymbolAction(ctx => AnalyzeType(ctx, names), SymbolKind.NamedType);
			start.RegisterSymbolAction(ctx => AnalyzeMethod(ctx, names), SymbolKind.Method);
			start.RegisterCompilationEndAction(end => ReportDuplicates(end, names));
		});
	}

	private static void AnalyzeType(SymbolAnalysisContext context, Dictionary<string, List<ISymbol>> names)
	{
		if (context.Symbol is not INamedTypeSymbol type)
			return;

		var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "McpToolAttribute");
		if (attr is null)
			return;

		var loc = type.Locations.FirstOrDefault() ?? Location.None;
		if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
			context.ReportDiagnostic(Diagnostic.Create(InvalidType, loc, type.Name));

		if (string.IsNullOrWhiteSpace(GetNamedString(attr, "Description")))
			context.ReportDiagnostic(Diagnostic.Create(MissingDescription, loc, type.Name));

		RememberName(names, attr, type);
	}

	private static void AnalyzeMethod(SymbolAnalysisContext context, Dictionary<string, List<ISymbol>> names)
	{
		if (context.Symbol is not IMethodSymbol method)
			return;

		var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "McpToolAttribute");
		if (attr is null)
			return;

		var loc = method.Locations.FirstOrDefault() ?? Location.None;
		if (!method.IsStatic)
			context.ReportDiagnostic(Diagnostic.Create(InstanceMethod, loc, method.Name));

		if (string.IsNullOrWhiteSpace(GetNamedString(attr, "Description")))
			context.ReportDiagnostic(Diagnostic.Create(MissingDescription, loc, method.Name));

		RememberName(names, attr, method);
	}

	private static void RememberName(Dictionary<string, List<ISymbol>> names, AttributeData attr, ISymbol symbol)
	{
		var nameArg = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
		if (string.IsNullOrWhiteSpace(nameArg))
			return;
		lock (names)
		{
			if (!names.TryGetValue(nameArg!, out var list))
			{
				list = [];
				names[nameArg!] = list;
			}

			list.Add(symbol);
		}
	}

	private static void ReportDuplicates(CompilationAnalysisContext context, Dictionary<string, List<ISymbol>> names)
	{
		foreach (var pair in names)
		{
			if (pair.Value.Count < 2)
				continue;
			foreach (var symbol in pair.Value)
			{
				var loc = symbol.Locations.FirstOrDefault() ?? Location.None;
				context.ReportDiagnostic(Diagnostic.Create(DuplicateName, loc, pair.Key));
			}
		}
	}

	private static string? GetNamedString(AttributeData attr, string name)
	{
		foreach (var arg in attr.NamedArguments)
		{
			if (arg.Key == name)
				return arg.Value.Value as string;
		}

		return null;
	}
}
