using System.Data;
using BuildingBlocks.Domain;
using Dapper;
using FeatureFusion.Domain.Catalog;

namespace FeatureFusion.Infrastructure.Dapper;

/// <summary>Registers Dapper type handlers for catalog identities and value objects.</summary>
public static class CatalogDapperTypeHandlers
{
	private static int _registered;

	/// <summary>Idempotent registration for process lifetime.</summary>
	public static void Register()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
			return;

		SqlMapper.AddTypeHandler(new IntIdentityHandler<ProductId>(ProductId.From));
		SqlMapper.AddTypeHandler(new IntIdentityHandler<BrandId>(BrandId.From));
		SqlMapper.AddTypeHandler(new IntIdentityHandler<CategoryId>(CategoryId.From));
		SqlMapper.AddTypeHandler(new StringValueHandler<Sku>(Sku.Create));
		SqlMapper.AddTypeHandler(new StringValueHandler<Slug>(Slug.Create));
	}

	private sealed class IntIdentityHandler<T> : SqlMapper.TypeHandler<T>
		where T : IIdentity<int>
	{
		private readonly Func<int, T> _factory;

		public IntIdentityHandler(Func<int, T> factory) => _factory = factory;

		public override T Parse(object value) => _factory(Convert.ToInt32(value));

		public override void SetValue(IDbDataParameter parameter, T? value)
		{
			parameter.DbType = DbType.Int32;
			parameter.Value = value is null ? DBNull.Value : value.Value;
		}
	}

	private sealed class StringValueHandler<T> : SqlMapper.TypeHandler<T>
	{
		private readonly Func<string, T> _factory;

		public StringValueHandler(Func<string, T> factory) => _factory = factory;

		public override T Parse(object value) => _factory(Convert.ToString(value) ?? "");

		public override void SetValue(IDbDataParameter parameter, T? value)
		{
			parameter.DbType = DbType.String;
			parameter.Value = value is null ? DBNull.Value : value.ToString();
		}
	}
}
