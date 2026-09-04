using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Builds Npgsql <c>EF.Functions.GreaterThan/LessThan(ValueTuple, ValueTuple)</c> seek predicates
/// when the provider can translate them to SQL row comparison.
/// </summary>
internal static class CursorSeekTuple
{
	private static readonly object Gate = new();
	private static MethodInfo? _greaterThan;
	private static MethodInfo? _lessThan;
	private static bool _resolved;

	public static bool TryBuild<T>(
		SortKey<T> sortKey,
		object?[] values,
		bool walkBackward,
		out Expression<Func<T, bool>> predicate)
	{
		predicate = null!;
		if (!CanUse(sortKey, walkBackward))
		{
			return false;
		}

		EnsureResolved();
		var method = SeekOps.UseGreater(sortKey.Slots[0], walkBackward) ? _greaterThan : _lessThan;
		if (method is null)
		{
			return false;
		}

		var parameter = Expression.Parameter(typeof(T), "x");
		var leftArgs = new Expression[sortKey.Slots.Count];
		var rightArgs = new Expression[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			var slot = sortKey.Slots[i];
			var body = CursorSlot.Body<object>(parameter, slot);
			if (body.Type != slot.DeclaredType)
			{
				body = Expression.Convert(body, slot.DeclaredType);
			}

			leftArgs[i] = body;
			rightArgs[i] = Constant(values[i], slot.DeclaredType);
		}

		var leftTuple = Expression.Convert(CreateValueTuple(leftArgs), typeof(ITuple));
		var rightTuple = Expression.Convert(CreateValueTuple(rightArgs), typeof(ITuple));
		var call = Expression.Call(
			method,
			Expression.Property(null, typeof(EF), nameof(EF.Functions)),
			leftTuple,
			rightTuple);

		predicate = Expression.Lambda<Func<T, bool>>(call, parameter);
		return true;
	}

	/// <summary>
	/// Row comparison needs at least two slots. There is no upper bound: 2–8 use
	/// <see cref="ValueTuple.Create"/>; 9+ nest the remainder as <c>TRest</c> (same shape as C# tuples).
	/// One-column keys stay on the simple comparison in <see cref="CursorSeek"/>.
	/// </summary>
	public static bool CanUse<T>(SortKey<T> sortKey, bool walkBackward)
		=> sortKey.Slots.Count >= 2
			&& SeekOps.TupleEligible(sortKey, walkBackward)
			&& SeekOps.TupleSlotsNonNull(sortKey);

	private static void EnsureResolved()
	{
		if (_resolved)
		{
			return;
		}

		lock (Gate)
		{
			if (_resolved)
			{
				return;
			}

			var type = Type.GetType(
				"Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL",
				throwOnError: false);
			if (type is not null)
			{
				_greaterThan = FindCompare(type, "GreaterThan");
				_lessThan = FindCompare(type, "LessThan");
			}

			_resolved = true;
		}
	}

	private static MethodInfo? FindCompare(Type extensions, string name)
	{
		foreach (var method in extensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
		{
			if (method.Name != name)
			{
				continue;
			}

			var parameters = method.GetParameters();
			if (parameters.Length != 3
				|| parameters[0].ParameterType != typeof(DbFunctions)
				|| !typeof(ITuple).IsAssignableFrom(parameters[1].ParameterType)
				|| !typeof(ITuple).IsAssignableFrom(parameters[2].ParameterType))
			{
				continue;
			}

			return method;
		}

		return null;
	}

	private static Expression CreateValueTuple(Expression[] args)
	{
		if (args.Length is < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(args));
		}

		if (args.Length <= 8)
		{
			var types = args.Select(a => a.Type).ToArray();
			var create = typeof(ValueTuple).GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(m => m.Name == nameof(ValueTuple.Create) && m.IsGenericMethodDefinition)
				.First(m => m.GetParameters().Length == args.Length)
				.MakeGenericMethod(types);
			return Expression.Call(create, args);
		}

		// 9+: ValueTuple<T1..T7, TRest>. Do not use Create(8) — it wraps the 8th argument in ValueTuple<T8>.
		var rest = CreateValueTuple(args[7..]);
		var typeArgs = args.Take(7).Select(a => a.Type).Append(rest.Type).ToArray();
		var tupleType = typeof(ValueTuple<,,,,,,,>).MakeGenericType(typeArgs);
		var ctor = tupleType.GetConstructors().Single(c => c.GetParameters().Length == 8);
		var ctorArgs = new Expression[8];
		Array.Copy(args, ctorArgs, 7);
		ctorArgs[7] = rest;
		return Expression.New(ctor, ctorArgs);
	}

	private static Expression Constant(object? value, Type declared)
	{
		if (value is null)
		{
			return Expression.Constant(null, declared);
		}

		var converted = declared.IsInstanceOfType(value)
			? value
			: Convert.ChangeType(value, Nullable.GetUnderlyingType(declared) ?? declared);
		return Expression.Constant(converted, declared);
	}
}
