using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BuildingBlocks.Mediator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WrongHandlerKindAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "BBM001";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		title: "Handler implements the wrong message kind",
		messageFormat: "Type '{0}' implements {1} but message '{2}' is not compatible with that handler kind",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Command handlers should target ICommand/ICommand<T>; query handlers should target IQuery<T>.",
		customTags: WellKnownDiagnosticTags.Telemetry);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
		=> ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
	}

	private static void AnalyzeNamedType(SymbolAnalysisContext context)
	{
		if (context.Symbol is not INamedTypeSymbol type || type.TypeKind != TypeKind.Class || type.IsAbstract)
			return;

		foreach (var iface in type.AllInterfaces)
		{
			if (!TryGetHandler(iface, out var kind, out var messageType) || messageType is null)
				continue;

			var ok = kind switch
			{
				HandlerKind.VoidCommand => IsVoidCommand(messageType),
				HandlerKind.Command => IsCommandWithResponse(messageType),
				HandlerKind.Query => IsQuery(messageType),
				_ => true
			};

			if (!ok)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					Rule,
					type.Locations.FirstOrDefault() ?? Location.None,
					type.Name,
					iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
					messageType.Name));
			}
		}
	}

	private static bool TryGetHandler(INamedTypeSymbol iface, out HandlerKind kind, out ITypeSymbol? messageType)
	{
		kind = default;
		messageType = null;
		if (!iface.IsGenericType)
			return false;

		var arity = iface.OriginalDefinition.Arity;
		var name = iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		if (name.Contains("ICommandHandler") && arity == 1)
		{
			kind = HandlerKind.VoidCommand;
			messageType = iface.TypeArguments[0];
			return true;
		}

		if (name.Contains("ICommandHandler") && arity == 2)
		{
			kind = HandlerKind.Command;
			messageType = iface.TypeArguments[0];
			return true;
		}

		if (name.Contains("IQueryHandler") && arity == 2)
		{
			kind = HandlerKind.Query;
			messageType = iface.TypeArguments[0];
			return true;
		}

		return false;
	}

	private static bool IsVoidCommand(ITypeSymbol type)
		=> type.AllInterfaces.Any(i =>
			!i.IsGenericType && i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith(".ICommand", StringComparison.Ordinal));

	private static bool IsCommandWithResponse(ITypeSymbol type)
	{
		// ICommand (void) also implements ICommand<Unit>; typed command handlers accept either.
		if (IsVoidCommand(type))
			return true;

		return type.AllInterfaces.Any(i =>
			i.IsGenericType
			&& i.OriginalDefinition.Arity == 1
			&& i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains("ICommand"));
	}

	private static bool IsQuery(ITypeSymbol type)
		=> type.AllInterfaces.Any(i =>
			i.IsGenericType
			&& i.OriginalDefinition.Arity == 1
			&& i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains("IQuery"));

	private enum HandlerKind
	{
		VoidCommand,
		Command,
		Query
	}
}
