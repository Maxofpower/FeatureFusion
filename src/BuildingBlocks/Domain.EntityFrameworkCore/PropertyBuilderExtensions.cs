using System.Linq.Expressions;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Domain.EntityFrameworkCore;

/// <summary>Fluent helpers for mapping typed identities and value objects.</summary>
public static class PropertyBuilderExtensions
{
	/// <summary>Maps an Identity property with a cached converter.</summary>
	public static PropertyBuilder<TIdentity> HasIdentityConversion<TIdentity, TId>(
		this PropertyBuilder<TIdentity> builder,
		Expression<Func<TId, TIdentity>> fromProvider)
		where TIdentity : Identity<TId>
		where TId : notnull
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.HasConversion(IdentityValueConverter.For(fromProvider));
	}

	/// <summary>Maps a nullable identity FK with a cached converter.</summary>
	public static PropertyBuilder<TIdentity?> HasNullableIdentityConversion<TIdentity, TId>(
		this PropertyBuilder<TIdentity?> builder,
		Expression<Func<TId, TIdentity>> fromProvider)
		where TIdentity : Identity<TId>
		where TId : struct
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.HasConversion(IdentityValueConverter.ForNullable(fromProvider));
	}

	/// <summary>Maps a ValueObject column with a cached converter.</summary>
	public static PropertyBuilder<TValueObject> HasValueObjectConversion<TValueObject, TPrimitive>(
		this PropertyBuilder<TValueObject> builder,
		Expression<Func<TValueObject, TPrimitive>> toProvider,
		Expression<Func<TPrimitive, TValueObject>> fromProvider)
		where TValueObject : ValueObject
		where TPrimitive : notnull
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.HasConversion(ValueObjectValueConverter.For(toProvider, fromProvider));
	}
}