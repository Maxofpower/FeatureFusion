using System.Linq.Expressions;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;

internal static class CursorSlot
{
	public static LambdaExpression Lambda<T>(SortSlot slot)
	{
		var parameter = Expression.Parameter(typeof(T), "x");
		var body = Body<T>(parameter, slot);
		if (body.Type != slot.DeclaredType)
		{
			body = Expression.Convert(body, slot.DeclaredType);
		}

		return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(T), slot.DeclaredType), body, parameter);
	}

	public static Expression Body<T>(ParameterExpression parameter, SortSlot slot)
	{
		if (slot.Kind == SortSlotKind.Shadow)
		{
			return Expression.Call(
				typeof(EF),
				nameof(EF.Property),
				[slot.DeclaredType],
				parameter,
				Expression.Constant(slot.ShadowName));
		}

		var visitor = new ReplaceParameter(slot.Accessor!.Parameters[0], parameter);
		var body = visitor.Visit(slot.Accessor.Body)!;
		return ExpressionPath.Strip(body);
	}

	private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node)
			=> node == from ? to : base.VisitParameter(node);
	}
}
