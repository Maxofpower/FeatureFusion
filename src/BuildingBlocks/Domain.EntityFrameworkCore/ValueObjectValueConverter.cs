using System.Collections.Concurrent;
using System.Linq.Expressions;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Domain.EntityFrameworkCore;

/// <summary>
/// Cached converters for single-field ValueObject types persisted as a primitive column.
/// </summary>
public static class ValueObjectValueConverter
{
	private static readonly ConcurrentDictionary<(Type Vo, Type Primitive), ValueConverter> Cache = new();

	/// <summary>
	/// Returns a cached converter using toProvider / fromProvider expressions.
	/// </summary>
	public static ValueConverter<TValueObject, TPrimitive> For<TValueObject, TPrimitive>(
		Expression<Func<TValueObject, TPrimitive>> toProvider,
		Expression<Func<TPrimitive, TValueObject>> fromProvider)
		where TValueObject : ValueObject
		where TPrimitive : notnull
	{
		ArgumentNullException.ThrowIfNull(toProvider);
		ArgumentNullException.ThrowIfNull(fromProvider);
		var key = (typeof(TValueObject), typeof(TPrimitive));
		return (ValueConverter<TValueObject, TPrimitive>)Cache.GetOrAdd(
			key,
			_ => new ValueConverter<TValueObject, TPrimitive>(toProvider, fromProvider));
	}
}