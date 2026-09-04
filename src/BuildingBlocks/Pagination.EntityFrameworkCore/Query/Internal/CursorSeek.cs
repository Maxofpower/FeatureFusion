using System.Linq.Expressions;
using BuildingBlocks.Pagination;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;

internal static class CursorSeek
{
	public static Expression<Func<T, bool>> Build<T>(
		SortKey<T> sortKey,
		object?[] values,
		bool walkBackward,
		NullOrder nulls)
	{
		var parameter = Expression.Parameter(typeof(T), "x");
		Expression? orChain = null;

		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			Expression prefix = Expression.Constant(true);
			for (var j = 0; j < i; j++)
			{
				prefix = And(prefix, Equal(parameter, sortKey.Slots[j], values[j]));
			}

			var cmp = Compare(parameter, sortKey.Slots[i], values[i], SeekOps.UseGreater(sortKey.Slots[i], walkBackward), nulls);
			var term = i == 0 ? cmp : And(prefix, cmp);
			orChain = orChain is null ? term : Expression.OrElse(orChain, term);
		}

		return Expression.Lambda<Func<T, bool>>(orChain!, parameter);
	}

	private static Expression And(Expression left, Expression right)
	{
		if (left is ConstantExpression { Value: true })
		{
			return right;
		}

		return Expression.AndAlso(left, right);
	}

	private static Expression Equal(ParameterExpression parameter, SortSlot slot, object? value)
	{
		var left = CursorSlot.Body<object>(parameter, slot);
		left = Align(left, slot.DeclaredType);
		var right = Constant(value, slot.DeclaredType);
		return Expression.Equal(left, right);
	}

	private static Expression Compare(
		ParameterExpression parameter,
		SortSlot slot,
		object? value,
		bool greater,
		NullOrder nulls)
	{
		var left = Align(CursorSlot.Body<object>(parameter, slot), slot.DeclaredType);
		var right = Constant(value, slot.DeclaredType);

		if (!IsNullableRefOrNullableValue(slot.DeclaredType))
		{
			return CompareNonNull(left, right, greater, slot.DeclaredType);
		}

		var leftNull = IsNull(left);
		var rightNull = IsNull(right);
		var bothNonNull = Expression.AndAlso(Expression.Not(leftNull), Expression.Not(rightNull));
		var valueCmp = CompareNonNull(left, right, greater, slot.DeclaredType);
		var nonNullCmp = Expression.AndAlso(bothNonNull, valueCmp);

		Expression nullCmp;
		if (nulls == NullOrder.Last)
		{
			// Null is largest: row > cursor includes null rows when cursor is non-null.
			nullCmp = greater
				? Expression.AndAlso(leftNull, Expression.Not(rightNull))
				: Expression.AndAlso(Expression.Not(leftNull), rightNull);
		}
		else
		{
			// Null is smallest: row > cursor includes non-null rows when cursor is null.
			nullCmp = greater
				? Expression.AndAlso(Expression.Not(leftNull), rightNull)
				: Expression.AndAlso(leftNull, Expression.Not(rightNull));
		}

		return Expression.OrElse(nonNullCmp, nullCmp);
	}

	private static Expression CompareNonNull(Expression left, Expression right, bool greater, Type declared)
	{
		var type = Nullable.GetUnderlyingType(declared) ?? declared;
		left = UnwrapNullable(left);
		right = UnwrapNullable(right);
		if (type == typeof(string))
		{
			// string.Compare translates to SQL `>` / `<` (database collation). CompareOrdinal does not translate.
			var compare = typeof(string).GetMethod(
				nameof(string.Compare),
				[typeof(string), typeof(string)])!;
			var call = Expression.Call(compare, left, right);
			var zero = Expression.Constant(0);
			return greater ? Expression.GreaterThan(call, zero) : Expression.LessThan(call, zero);
		}

		if (type.IsEnum)
		{
			var underlying = Enum.GetUnderlyingType(type);
			left = Expression.Convert(left, underlying);
			right = Expression.Convert(right, underlying);
			return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
		}

		if (type == typeof(bool))
		{
			left = Expression.Convert(left, typeof(int));
			right = Expression.Convert(right, typeof(int));
			return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
		}

		return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
	}

	private static Expression UnwrapNullable(Expression expr)
		=> Nullable.GetUnderlyingType(expr.Type) is { } inner
			? Expression.Convert(expr, inner)
			: expr;

	private static Expression Align(Expression body, Type declared)
		=> body.Type == declared ? body : Expression.Convert(body, declared);

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

	private static Expression IsNull(Expression expr)
	{
		if (expr.Type.IsValueType && Nullable.GetUnderlyingType(expr.Type) is null)
		{
			return Expression.Constant(false);
		}

		return Expression.Equal(expr, Expression.Constant(null, expr.Type));
	}

	private static bool IsNullableRefOrNullableValue(Type type)
		=> !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
