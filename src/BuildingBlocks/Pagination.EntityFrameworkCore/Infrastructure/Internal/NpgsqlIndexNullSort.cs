using System.Reflection;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;

/// <summary>
/// Soft-calls Npgsql <c>HasNullSortOrder</c> when that assembly is already loaded.
/// Same resolution style as row-comparison seek (no PackageReference).
/// </summary>
internal static class NpgsqlIndexNullSort
{
	private static readonly object Gate = new();
	private static MethodInfo? _hasNullSortOrder;
	private static Type? _nullSortOrderEnum;

	internal static void TryApply(IndexBuilder index, int columnCount, NullOrder nulls)
	{
		EnsureResolved();
		if (_hasNullSortOrder is null || _nullSortOrderEnum is null)
		{
			return;
		}

		var enumName = nulls == NullOrder.First ? "NullsFirst" : "NullsLast";
		if (Enum.GetNames(_nullSortOrderEnum).All(n => n != enumName))
		{
			return;
		}

		var value = Enum.Parse(_nullSortOrderEnum, enumName);
		var values = Array.CreateInstance(_nullSortOrderEnum, columnCount);
		for (var i = 0; i < columnCount; i++)
		{
			values.SetValue(value, i);
		}

		_hasNullSortOrder.Invoke(null, [index, values]);
	}

	private static void EnsureResolved()
	{
		if (_hasNullSortOrder is not null && _nullSortOrderEnum is not null)
		{
			return;
		}

		lock (Gate)
		{
			if (_hasNullSortOrder is not null && _nullSortOrderEnum is not null)
			{
				return;
			}

			var extensions = Type.GetType(
				"Microsoft.EntityFrameworkCore.NpgsqlIndexBuilderExtensions, " + CursorProvider.Npgsql,
				throwOnError: false);
			_nullSortOrderEnum = Type.GetType(
				"Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NullSortOrder, " + CursorProvider.Npgsql,
				throwOnError: false);
			if (extensions is not null)
			{
				_hasNullSortOrder = FindHasNullSortOrder(extensions);
			}
		}
	}

	private static MethodInfo? FindHasNullSortOrder(Type extensions)
	{
		foreach (var method in extensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
		{
			if (method.Name != "HasNullSortOrder" || method.IsGenericMethod)
			{
				continue;
			}

			var parameters = method.GetParameters();
			if (parameters.Length == 2
				&& parameters[0].ParameterType == typeof(IndexBuilder)
				&& parameters[1].ParameterType.IsArray)
			{
				return method;
			}
		}

		return null;
	}
}
