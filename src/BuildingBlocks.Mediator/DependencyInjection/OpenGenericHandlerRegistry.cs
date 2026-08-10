using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.DependencyInjection;

/// <summary>
/// Tracks open-generic command/query handlers discovered by the assembly scanner.
/// MS DI cannot register handlers like <c>Handler&lt;T&gt; : ICommandHandler&lt;EchoCommand&lt;T&gt;, T&gt;</c>
/// as native open generics (arity mismatch), so we close them on demand.
/// </summary>
internal sealed class OpenGenericHandlerRegistry
{
	private readonly List<OpenHandlerEntry> _entries = new();
	private readonly ConcurrentDictionary<Type, List<Type>> _closedImplCache = new();

	public void Add(Type openHandlerType, Type openHandlerInterface)
	{
		ArgumentNullException.ThrowIfNull(openHandlerType);
		ArgumentNullException.ThrowIfNull(openHandlerInterface);

		if (!openHandlerType.IsGenericTypeDefinition)
			throw new ArgumentException("Handler type must be an open generic type definition.", nameof(openHandlerType));

		if (!openHandlerInterface.IsGenericType)
			throw new ArgumentException("Handler interface must be generic.", nameof(openHandlerInterface));

		_entries.Add(new OpenHandlerEntry(openHandlerType, openHandlerInterface));
	}

	public bool HasEntries => _entries.Count > 0;

	/// <summary>
	/// Returns true when at least one open-generic handler can be closed to satisfy
	/// <paramref name="closedHandlerInterface"/>.
	/// </summary>
	public bool CanSatisfy(Type closedHandlerInterface)
	{
		ArgumentNullException.ThrowIfNull(closedHandlerInterface);
		return ResolveAllClosedImplementations(closedHandlerInterface).Count > 0;
	}

	/// <summary>
	/// Creates closed handler instances that unify with <paramref name="closedHandlerInterface"/>.
	/// Open-generic handlers are always created as Transient via <see cref="ActivatorUtilities"/>.
	/// </summary>
	public IReadOnlyList<object> CreateMatches(IServiceProvider serviceProvider, Type closedHandlerInterface)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);
		ArgumentNullException.ThrowIfNull(closedHandlerInterface);

		var matches = new List<object>();
		foreach (var closedImpl in ResolveAllClosedImplementations(closedHandlerInterface))
		{
			matches.Add(ActivatorUtilities.CreateInstance(serviceProvider, closedImpl));
		}

		return matches;
	}

	private List<Type> ResolveAllClosedImplementations(Type closedHandlerInterface)
	{
		if (!closedHandlerInterface.IsGenericType || closedHandlerInterface.IsGenericTypeDefinition)
			return [];

		// Cache the resolved closed impl list as a single Type when exactly one match,
		// or a sentinel multi/empty via recomputation when needed. Prefer caching the list key.
		return _closedImplCache.GetOrAdd(closedHandlerInterface, static (iface, registry) =>
			registry.ComputeClosedImplementations(iface), this);
	}

	private List<Type> ComputeClosedImplementations(Type closedHandlerInterface)
	{
		var definition = closedHandlerInterface.GetGenericTypeDefinition();
		var closedArgs = closedHandlerInterface.GetGenericArguments();
		var results = new List<Type>();

		foreach (var entry in _entries)
		{
			var openIfaceDef = entry.OpenHandlerInterface.IsGenericTypeDefinition
				? entry.OpenHandlerInterface
				: entry.OpenHandlerInterface.GetGenericTypeDefinition();

			if (openIfaceDef != definition)
				continue;

			if (!TryInferHandlerTypeArgs(entry, closedArgs, out var handlerTypeArgs))
				continue;

			try
			{
				results.Add(entry.OpenHandlerType.MakeGenericType(handlerTypeArgs));
			}
			catch (ArgumentException)
			{
				// Constraint mismatch — skip this candidate.
			}
		}

		return results;
	}

	private static bool TryInferHandlerTypeArgs(
		OpenHandlerEntry entry,
		Type[] closedInterfaceArgs,
		out Type[] handlerTypeArgs)
	{
		handlerTypeArgs = entry.OpenHandlerType.GetGenericArguments();
		var inferred = new Type?[handlerTypeArgs.Length];
		var openIfaceArgs = entry.OpenHandlerInterface.GetGenericArguments();

		if (openIfaceArgs.Length != closedInterfaceArgs.Length)
			return false;

		for (var i = 0; i < openIfaceArgs.Length; i++)
		{
			if (!TryUnify(openIfaceArgs[i], closedInterfaceArgs[i], inferred))
				return false;
		}

		handlerTypeArgs = new Type[inferred.Length];
		for (var i = 0; i < inferred.Length; i++)
		{
			if (inferred[i] is null)
				return false;
			handlerTypeArgs[i] = inferred[i]!;
		}

		return true;
	}

	/// <summary>
	/// Unifies an open type pattern against a closed type, filling <paramref name="inferred"/>.
	/// </summary>
	private static bool TryUnify(Type openPattern, Type closed, Type?[] inferred)
	{
		if (openPattern.IsGenericParameter)
		{
			var index = openPattern.GenericParameterPosition;
			if (inferred[index] is null)
			{
				inferred[index] = closed;
				return true;
			}

			return inferred[index] == closed;
		}

		if (openPattern.IsGenericType)
		{
			if (!closed.IsGenericType)
				return false;

			var openDef = openPattern.GetGenericTypeDefinition();
			var closedDef = closed.GetGenericTypeDefinition();
			if (openDef != closedDef)
				return false;

			var openArgs = openPattern.GetGenericArguments();
			var closedArgs = closed.GetGenericArguments();
			if (openArgs.Length != closedArgs.Length)
				return false;

			for (var i = 0; i < openArgs.Length; i++)
			{
				if (!TryUnify(openArgs[i], closedArgs[i], inferred))
					return false;
			}

			return true;
		}

		return openPattern == closed;
	}

	private readonly record struct OpenHandlerEntry(Type OpenHandlerType, Type OpenHandlerInterface);
}
