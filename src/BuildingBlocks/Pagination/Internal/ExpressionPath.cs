namespace BuildingBlocks.Pagination;

internal static class ExpressionPath
{
	public static string Get(LambdaExpression expression)
	{
		var body = Strip(expression.Body);
		var parts = new Stack<string>();
		while (body is MemberExpression member)
		{
			parts.Push(member.Member.Name);
			body = Strip(member.Expression);
		}

		if (parts.Count == 0)
		{
			throw new ArgumentException("Sort accessor must be a property chain (e.g. p => p.Price).", nameof(expression));
		}

		return string.Join('.', parts);
	}

	public static Expression Strip(Expression? expression)
	{
		while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
		{
			expression = unary.Operand;
		}

		return expression ?? throw new ArgumentException("Expression body was null.");
	}

	public static LambdaExpression Box<T, TValue>(Expression<Func<T, TValue>> accessor)
	{
		Expression body = accessor.Body;
		if (body.Type.IsValueType)
		{
			body = Expression.Convert(body, typeof(object));
		}

		return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(T), typeof(object)), body, accessor.Parameters);
	}
}
