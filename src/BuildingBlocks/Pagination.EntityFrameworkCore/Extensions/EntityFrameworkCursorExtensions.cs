using System.Linq.Expressions;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;
using BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Pagination.EntityFrameworkCore;

/// <summary>Keyset pagination for <see cref="IQueryable{T}"/> (EF Core).</summary>
public static class EntityFrameworkCursorExtensions
{
	/// <summary>Pages <paramref name="query"/> using <paramref name="sortKey"/>.</summary>
	/// <typeparam name="T">Entity type.</typeparam>
	/// <param name="query">Filtered query. Any host <c>OrderBy</c> is replaced, not merged.</param>
	/// <param name="request">Cursor and limit.</param>
	/// <param name="sortKey">Prebuilt sort key.</param>
	/// <param name="options">Optional options. <see cref="PaginationOptions.Hint"/> defaults to <see cref="QueryHint.None"/>.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	public static ValueTask<CursorPage<T>> ToCursorPageAsync<T>(
		this IQueryable<T> query,
		CursorRequest request,
		SortKey<T> sortKey,
		PaginationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= PaginationOptions.Default;
		return QueryHintExecutor.RunAsync(
			query,
			options,
			ct => ExecuteAsync(query, request, sortKey, options, ct),
			cancellationToken);
	}

	/// <summary>Pages then projects in SQL. <typeparamref name="TResult"/> must expose the keyset members.</summary>
	/// <typeparam name="T">Entity type.</typeparam>
	/// <typeparam name="TResult">DTO type.</typeparam>
	/// <param name="query">Filtered query. Any host <c>OrderBy</c> is replaced, not merged.</param>
	/// <param name="request">Cursor and limit.</param>
	/// <param name="sortKey">Sort key on <typeparamref name="T"/>.</param>
	/// <param name="selector">SQL projection.</param>
	/// <param name="options">Optional options. <see cref="PaginationOptions.Hint"/> defaults to <see cref="QueryHint.None"/>.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	public static ValueTask<CursorPage<TResult>> ToCursorPageAsync<T, TResult>(
		this IQueryable<T> query,
		CursorRequest request,
		SortKey<T> sortKey,
		Expression<Func<T, TResult>> selector,
		PaginationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= PaginationOptions.Default;
		return QueryHintExecutor.RunAsync(
			query,
			options,
			ct => ExecuteProjectedAsync(query, request, sortKey, selector, options, ct),
			cancellationToken);
	}

	/// <summary>Pages then maps in memory (e.g. lab <c>ToDto()</c>).</summary>
	/// <typeparam name="T">Entity type.</typeparam>
	/// <typeparam name="TResult">DTO type.</typeparam>
	/// <param name="query">Filtered query. Any host <c>OrderBy</c> is replaced, not merged.</param>
	/// <param name="request">Cursor and limit.</param>
	/// <param name="sortKey">Sort key on <typeparamref name="T"/>.</param>
	/// <param name="map">In-memory map.</param>
	/// <param name="options">Optional options. <see cref="PaginationOptions.Hint"/> defaults to <see cref="QueryHint.None"/>.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	public static async ValueTask<CursorPage<TResult>> ToCursorPageMappedAsync<T, TResult>(
		this IQueryable<T> query,
		CursorRequest request,
		SortKey<T> sortKey,
		Func<T, TResult> map,
		PaginationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var page = await query.ToCursorPageAsync(request, sortKey, options, cancellationToken).ConfigureAwait(false);
		var mapped = new List<TResult>(page.Items.Count);
		for (var i = 0; i < page.Items.Count; i++)
		{
			mapped.Add(map(page.Items[i]));
		}

		return new CursorPage<TResult>(
			mapped,
			page.Next,
			page.Previous,
			page.HasNext,
			page.HasPrevious,
			page.TotalCount);
	}

	private static async Task<CursorPage<T>> ExecuteAsync<T>(
		IQueryable<T> query,
		CursorRequest request,
		SortKey<T> sortKey,
		PaginationOptions options,
		CancellationToken cancellationToken)
	{
		var (walkBackward, values, fromCursor) = RequestCursor.Resolve(request, sortKey, options);
		CursorDbContext.EnsureShadowProperties(query, sortKey);

		int? total = null;
		if (options.IncludeTotalCount)
		{
			total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
		}

		var ordered = CursorOrder.Apply(query, sortKey, walkBackward);
		if (values is not null)
		{
			ordered = ordered.Where(CursorSeek.Build(sortKey, values, walkBackward, options.Nulls));
		}

		var fetched = await ordered.Take(request.Limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
		var ctx = CursorDbContext.TryGet(query);
		var keys = new List<object?[]>(fetched.Count);
		for (var i = 0; i < fetched.Count; i++)
		{
			keys.Add(CursorDbContext.ExtractKeys(fetched[i], sortKey, ctx));
		}

		return PageAssembler.Assemble(fetched, keys, sortKey, request.Limit, walkBackward, fromCursor, options, total);
	}

	private static async Task<CursorPage<TResult>> ExecuteProjectedAsync<T, TResult>(
		IQueryable<T> query,
		CursorRequest request,
		SortKey<T> sortKey,
		Expression<Func<T, TResult>> selector,
		PaginationOptions options,
		CancellationToken cancellationToken)
	{
		var (walkBackward, values, fromCursor) = RequestCursor.Resolve(request, sortKey, options);
		CursorDbContext.EnsureShadowProperties(query, sortKey);

		int? total = null;
		if (options.IncludeTotalCount)
		{
			total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
		}

		var ordered = CursorOrder.Apply(query, sortKey, walkBackward);
		if (values is not null)
		{
			ordered = ordered.Where(CursorSeek.Build(sortKey, values, walkBackward, options.Nulls));
		}

		var dtos = await ordered.Take(request.Limit + 1).Select(selector).ToListAsync(cancellationToken).ConfigureAwait(false);
		var projectedKey = new SortKey<TResult>(sortKey.Slots);
		var keys = new List<object?[]>(dtos.Count);
		for (var i = 0; i < dtos.Count; i++)
		{
			keys.Add(KeyExtractor.GetValuesFromMap(dtos[i], projectedKey));
		}

		return PageAssembler.Assemble(dtos, keys, projectedKey, request.Limit, walkBackward, fromCursor, options, total);
	}

	internal static string DebugQueryString<T>(IQueryable<T> query, SortKey<T> sortKey, bool walkBackward, int? take = null)
	{
		IQueryable<T> ordered = CursorOrder.Apply(query, sortKey, walkBackward);
		if (take is int n)
		{
			ordered = ordered.Take(n);
		}

		return ordered.ToQueryString();
	}

	internal static string DebugExpressionString<T>(IQueryable<T> query, SortKey<T> sortKey, bool walkBackward)
		=> CursorOrder.Apply(query, sortKey, walkBackward).Expression.ToString();

	internal static string DebugSeekQueryString<T>(
		IQueryable<T> query,
		SortKey<T> sortKey,
		object?[] values,
		bool walkBackward,
		NullOrder nulls = NullOrder.Last)
		=> CursorOrder.Apply(query, sortKey, walkBackward)
			.Where(CursorSeek.Build(sortKey, values, walkBackward, nulls))
			.ToQueryString();
}
