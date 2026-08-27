using FeatureFusion.Infrastructure.CursorPagination;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using static FeatureFusion.Infrastructure.CursorPagination.CursorFactory;
using FeatureFusion.Features.Products.Queries;

/// <summary>
/// Generic bidirectional keyset (cursor) pagination for EF Core queries.
/// </summary>
/// <remarks>
/// LinkedIn: Reusable Cursor (keyset) Pagination —
/// https://www.linkedin.com/feed/update/urn:li:activity:7325068550614708225/
/// Catalog: docs/linkedin-posts.md (<c>cursor-pagination</c>).
/// </remarks>
public static class PaginationHelper
{
	private static readonly JsonSerializerOptions _serializerOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		Converters = { new JsonStringEnumConverter() }
	};

	public static async Task<Result<PagedResult<TDto>>> PaginateAsync<TEntity, TDto, TSortField>(
		IQueryable<TEntity> query,
		int limit,
		TSortField sortField,
		SortDirection direction,
		string cursor,
		Func<TEntity, TDto> mapToDto,
		Func<TSortField, string> getSortFieldName,
		Func<string, TSortField> parseSortField,
		CancellationToken cancellationToken = default) where TEntity : class
	{
		try
		{
			if (limit <= 0 || limit > 100)
			{
				return Result<PagedResult<TDto>>.Failure(
					"Limit must be between 1 and 100",
					StatusCodes.Status400BadRequest);
			}

			var sortBy = getSortFieldName(sortField);
			var currentPage = 1;
			CursorData cursorData = null;

			if (!string.IsNullOrEmpty(cursor))
			{
				var cursorResult = DecodeAndValidateCursor<TEntity>(cursor, sortBy);
				if (!cursorResult.IsSuccess)
				{
					return Result<PagedResult<TDto>>.Failure(
						cursorResult.Error,
						cursorResult.StatusCode);
				}
				cursorData = cursorResult.Value;
				currentPage = cursorData.PageIndex;
			}

			// Determine sort direction based on cursor or original direction
			var sortDirection = cursorData?.Direction ?? direction;
			var sortedQuery = ApplySorting(query, sortBy, sortDirection);

			IQueryable<TEntity> filteredQuery = cursorData != null
				? ApplyCursorFilter(sortedQuery, cursorData, sortBy, direction)
				: sortedQuery;

			var results = await filteredQuery
				.Take(limit + 1)
				.ToListAsync(cancellationToken);
			var isPrev = cursorData != null && cursorData.Direction != direction;
			if (isPrev)
			{
				results.Reverse();
			}

			var (items, hasMore) = ProcessResults(isPrev, results, limit);

			string nextCursor = GenerateCursor(items.LastOrDefault(), currentPage + 1, sortBy, direction, hasMore);
			string previousCursor = GenerateCursor(items.FirstOrDefault(), currentPage - 1, sortBy, InvertDirection(direction), currentPage > 1);

			return Result<PagedResult<TDto>>.Success(new PagedResult<TDto>(
				Items: items.Select(mapToDto).ToList(),
				NextCursor: nextCursor,
				PreviousCursor: previousCursor,
				HasMore:  hasMore,
				HasPrevious: currentPage > 1,
				TotalCount: currentPage == 1 ? await query.CountAsync(cancellationToken) : 0));
		}
		catch (Exception ex)
		{
			return Result<PagedResult<TDto>>.Failure(
				$"Internal server error: {ex.Message}",
				StatusCodes.Status500InternalServerError);
		}
	}

	private static string GenerateCursor<TEntity>(
		TEntity item,
		int pageIndex,
		string sortBy,
		SortDirection direction,
		bool shouldGenerate)
	{
		if (!shouldGenerate || item == null || pageIndex < 1) return string.Empty;

		var value = typeof(TEntity).GetProperty(sortBy)?.GetValue(item);
		var id = (int?)typeof(TEntity).GetProperty("Id")?.GetValue(item);

		if (value == null || id == null) return string.Empty;

		return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
			new CursorData(value, id.Value, sortBy, direction, pageIndex),
			_serializerOptions)));
	}

	private static Result<CursorData> DecodeAndValidateCursor<TEntity>(
		string cursor,
		string expectedSortBy)
	{
		try
		{
			var cursorData = JsonSerializer.Deserialize<CursorData>(
				Encoding.UTF8.GetString(Convert.FromBase64String(cursor)),
				_serializerOptions);

			if (cursorData.SortBy != expectedSortBy)
			{
				return Result<CursorData>.Failure(
					"Cursor sort field mismatch",
					StatusCodes.Status400BadRequest);
			}

			var propertyInfo = typeof(TEntity).GetProperty(cursorData.SortBy);
			if (propertyInfo == null)
			{
				return Result<CursorData>.Failure(
					"Invalid sort field in cursor",
					StatusCodes.Status400BadRequest);
			}

			object lastValue;
			if (cursorData.LastValue is JsonElement jsonElement)
			{
				lastValue = JsonSerializer.Deserialize(
					jsonElement.GetRawText(),
					propertyInfo.PropertyType,
					_serializerOptions);
			}
			else
			{
				lastValue = cursorData.LastValue;
			}

			return Result<CursorData>.Success(new CursorData(
				lastValue,
				cursorData.LastId,
				cursorData.SortBy,
				cursorData.Direction,
				cursorData.PageIndex));
		}
		catch
		{
			return Result<CursorData>.Failure(
				"Invalid cursor format",
				StatusCodes.Status400BadRequest);
		}
	}

	private static IQueryable<TEntity> ApplySorting<TEntity>(
		IQueryable<TEntity> query,
		string sortBy,
		SortDirection direction)
	{
		return direction == SortDirection.Ascending
			? query.OrderBy(x => EF.Property<object>(x, sortBy))
			: query.OrderByDescending(x => EF.Property<object>(x, sortBy));
	}

	private static IQueryable<TEntity> ApplyCursorFilter<TEntity>(
		IQueryable<TEntity> query,
		CursorData cursor,
		string sortBy,
		SortDirection originalDirection)
	{
		if (cursor == null) return query;

		var parameter = Expression.Parameter(typeof(TEntity), "x");
		var property = Expression.Property(parameter, sortBy);
		var value = Expression.Constant(cursor.LastValue);
		var idProperty = Expression.Property(parameter, "Id");
		var idValue = Expression.Constant(cursor.LastId);

		var propertyType = typeof(TEntity).GetProperty(sortBy)?.PropertyType;
		bool isPreviousCursor = cursor.Direction != originalDirection;

		Expression comparison;
		Expression idCompare;

		if (propertyType == typeof(string))
		{
			var compareMethod = typeof(string).GetMethod("CompareTo", new[] { typeof(string) });
			var left = property;
			var right = value;

			if (isPreviousCursor)
			{
				comparison = originalDirection == SortDirection.Ascending
					? Expression.LessThan(
						Expression.Call(left, compareMethod, right),
						Expression.Constant(0))
					: Expression.GreaterThan(
						Expression.Call(left, compareMethod, right),
						Expression.Constant(0));
			}
			else
			{
				comparison = originalDirection == SortDirection.Ascending
					? Expression.GreaterThan(
						Expression.Call(left, compareMethod, right),
						Expression.Constant(0))
					: Expression.LessThan(
						Expression.Call(left, compareMethod, right),
						Expression.Constant(0));
			}
		}
		else
		{
			if (isPreviousCursor)
			{
				comparison = originalDirection == SortDirection.Ascending
					? Expression.LessThan(property, value)
					: Expression.GreaterThan(property, value);
			}
			else
			{
				comparison = originalDirection == SortDirection.Ascending
					? Expression.GreaterThan(property, value)
					: Expression.LessThan(property, value);
			}
		}

		idCompare = originalDirection == SortDirection.Ascending
			? (isPreviousCursor
				? Expression.LessThan(idProperty, idValue)
				: Expression.GreaterThan(idProperty, idValue))
			: (isPreviousCursor
				? Expression.GreaterThan(idProperty, idValue)
				: Expression.LessThan(idProperty, idValue));

			var equal = Expression.Equal(property, value);
			var combined = Expression.OrElse(comparison, Expression.AndAlso(equal, idCompare));
			var lambdaFwd = Expression.Lambda<Func<TEntity, bool>>(combined, parameter);
			return query.Where(lambdaFwd);


	}

	private static (List<TEntity> Items, bool HasMore) ProcessResults<TEntity>(bool isPrev,
		List<TEntity> results,
		int limit)
	{
		var hasMore = results.Count > limit;

		if(isPrev)
		{
			return (hasMore ? results.Skip(1).Take(limit).ToList() : results, true);
		}
		return (hasMore ? results.Take(limit).ToList() : results, hasMore);
	}

	private static SortDirection InvertDirection(SortDirection direction)
	{
		return direction == SortDirection.Ascending
			? SortDirection.Descending
			: SortDirection.Ascending;
	}

	public record CursorData(object LastValue, int LastId, string SortBy, SortDirection Direction, int PageIndex);
}