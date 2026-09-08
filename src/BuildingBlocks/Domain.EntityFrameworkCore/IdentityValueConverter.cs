using System.Collections.Concurrent;
using System.Linq.Expressions;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Domain.EntityFrameworkCore;

/// <summary>
/// Cached ValueConverter instances for Identity types.
/// One converter instance is shared per (identity, primitive) pair.
/// </summary>
public static class IdentityValueConverter
{
	private static readonly ConcurrentDictionary<(Type Identity, Type Primitive), ValueConverter> Cache = new();

	/// <summary>
	/// Returns a cached converter: identity to primitive via Value, primitive to identity via fromProvider.
	/// </summary>
	public static ValueConverter<TIdentity, TId> For<TIdentity, TId>(Expression<Func<TId, TIdentity>> fromProvider)
		where TIdentity : Identity<TId>
		where TId : notnull
	{
		ArgumentNullException.ThrowIfNull(fromProvider);
		var key = (typeof(TIdentity), typeof(TId));
		return (ValueConverter<TIdentity, TId>)Cache.GetOrAdd(
			key,
			_ => new ValueConverter<TIdentity, TId>(
				id => id.Value,
				fromProvider));
	}

	/// <summary>
	/// Cached nullable converter for optional FK identity properties (BrandId? to int?).
	/// </summary>
	public static ValueConverter<TIdentity?, TId?> ForNullable<TIdentity, TId>(Expression<Func<TId, TIdentity>> fromProvider)
		where TIdentity : Identity<TId>
		where TId : struct
	{
		ArgumentNullException.ThrowIfNull(fromProvider);
		var factory = fromProvider.Compile();
		var key = (typeof(TIdentity), typeof(Nullable<>).MakeGenericType(typeof(TId)));
		return (ValueConverter<TIdentity?, TId?>)Cache.GetOrAdd(
			key,
			_ => new ValueConverter<TIdentity?, TId?>(
				id => id == null ? null : id.Value,
				value => value.HasValue ? factory(value.Value) : null));
	}
}