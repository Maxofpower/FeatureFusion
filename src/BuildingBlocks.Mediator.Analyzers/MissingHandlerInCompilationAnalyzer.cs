using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BuildingBlocks.Mediator.Analyzers;

/// <summary>
/// Reports when a message type in this compilation has no matching handler in the same compilation.
/// Cross-assembly handlers are fine — severity is Information, not Error. Prefer ValidateOnStartup for authoritative checks.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingHandlerInCompilationAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "BBM002";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		title: "Message has no handler in this compilation",
		messageFormat: "'{0}' has no matching handler in this compilation (handlers in other projects are fine; use ValidateOnStartup for runtime checks)",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: "Same-compilation helper only. Does not replace ValidateOnStartup.",
		customTags: WellKnownDiagnosticTags.CompilationEnd);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
		=> ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationAction(AnalyzeCompilation);
	}

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		var messages = new List<(INamedTypeSymbol Type, string ExpectedKey)>();
		var handlers = new HashSet<string>(StringComparer.Ordinal);

		foreach (var type in GetAllTypes(context.Compilation.Assembly.GlobalNamespace))
		{
			if (type.TypeKind != TypeKind.Class || type.IsAbstract)
				continue;

			if (type.IsGenericType)
			{
				CollectOpenGenericHandlers(type, handlers);
				continue;
			}

			CollectMessage(type, messages);
			CollectHandlers(type, handlers);
		}

		foreach (var (message, expected) in messages)
		{
			if (handlers.Contains(expected) || HandlersCover(handlers, expected))
				continue;

			context.ReportDiagnostic(Diagnostic.Create(
				Rule,
				message.Locations.FirstOrDefault() ?? Location.None,
				message.Name));
		}
	}

	private static bool HandlersCover(HashSet<string> handlers, string expected)
	{
		var sep = expected.LastIndexOf('|');
		if (sep < 0)
			return false;

		var wildcard = expected.Substring(0, sep) + "|*";
		return handlers.Contains(wildcard);
	}

	private static void CollectMessage(INamedTypeSymbol type, List<(INamedTypeSymbol, string)> messages)
	{
		if (IsVoidCommand(type))
		{
			messages.Add((type, "void:" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
			return;
		}

		var command = type.AllInterfaces.FirstOrDefault(i =>
			i.IsGenericType
			&& i.OriginalDefinition.Arity == 1
			&& i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains("ICommand"));
		if (command is not null)
		{
			messages.Add((type,
				"cmd:" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
				command.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
			return;
		}

		var query = type.AllInterfaces.FirstOrDefault(i =>
			i.IsGenericType
			&& i.OriginalDefinition.Arity == 1
			&& i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains("IQuery"));
		if (query is not null)
		{
			messages.Add((type,
				"query:" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
				query.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
		}
	}

	private static void CollectHandlers(INamedTypeSymbol type, HashSet<string> handlers)
	{
		foreach (var iface in type.AllInterfaces)
			AddHandlerKey(iface, handlers);
	}

	private static void CollectOpenGenericHandlers(INamedTypeSymbol type, HashSet<string> handlers)
	{
		if (!type.IsGenericType)
			return;

		foreach (var iface in type.AllInterfaces)
		{
			if (!iface.IsGenericType)
				continue;

			var def = iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (!def.Contains("ICommandHandler") && !def.Contains("IQueryHandler"))
				continue;

			var messageArg = iface.TypeArguments[0];
			if (messageArg.TypeKind == TypeKind.TypeParameter)
				continue;

			AddHandlerKey(iface, handlers, allowWildcardResponse: true);
		}
	}

	private static void AddHandlerKey(
		INamedTypeSymbol iface,
		HashSet<string> handlers,
		bool allowWildcardResponse = false)
	{
		if (!iface.IsGenericType)
			return;

		var def = iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		if (def.Contains("ICommandHandler") && iface.OriginalDefinition.Arity == 1)
		{
			handlers.Add("void:" + iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		}
		else if (def.Contains("ICommandHandler") && iface.OriginalDefinition.Arity == 2)
		{
			var response = iface.TypeArguments[1];
			var responseKey = allowWildcardResponse && response.TypeKind == TypeKind.TypeParameter
				? "*"
				: response.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			handlers.Add("cmd:" + iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
			             responseKey);
		}
		else if (def.Contains("IQueryHandler") && iface.OriginalDefinition.Arity == 2)
		{
			var response = iface.TypeArguments[1];
			var responseKey = allowWildcardResponse && response.TypeKind == TypeKind.TypeParameter
				? "*"
				: response.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			handlers.Add("query:" + iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
			             responseKey);
		}
	}

	private static bool IsVoidCommand(INamedTypeSymbol type)
		=> type.AllInterfaces.Any(i =>
			!i.IsGenericType && i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith(".ICommand", StringComparison.Ordinal));

	private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
	{
		foreach (var type in ns.GetTypeMembers())
		{
			yield return type;
			foreach (var nested in GetNested(type))
				yield return nested;
		}

		foreach (var child in ns.GetNamespaceMembers())
		{
			foreach (var type in GetAllTypes(child))
				yield return type;
		}
	}

	private static IEnumerable<INamedTypeSymbol> GetNested(INamedTypeSymbol type)
	{
		foreach (var nested in type.GetTypeMembers())
		{
			yield return nested;
			foreach (var deeper in GetNested(nested))
				yield return deeper;
		}
	}
}
