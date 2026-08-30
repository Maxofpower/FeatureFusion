using BuildingBlocks.Pagination;
using FeatureFusion.Domain.Entities;
using FeatureFusion.Features.Products.Queries;
using SortDirection = FeatureFusion.Features.Products.Queries.SortDirection;

namespace FeatureFusion.Infrastructure.Pagination;

/// <summary>
/// Prebuilt <see cref="SortKey{T}"/> instances for every <see cref="ProductSortField"/>.
/// Hosts map enums to keys; the library never reflects a property name from a string.
/// </summary>
public static class ProductSortKeys
{
	public static readonly SortKey<Product> IdAsc =
		SortKey.For<Product>().ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> IdDesc =
		SortKey.For<Product>().ThenByUniqueDescending(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> NameAsc =
		SortKey.For<Product>().By(p => p.Name, sql: "Name").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> NameDesc =
		SortKey.For<Product>().ByDescending(p => p.Name, sql: "Name").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> PriceAsc =
		SortKey.For<Product>().By(p => p.Price, sql: "Price").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> PriceDesc =
		SortKey.For<Product>().ByDescending(p => p.Price, sql: "Price").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> CreatedAtAsc =
		SortKey.For<Product>().By(p => p.CreatedAt, sql: "CreatedAt").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKey<Product> CreatedAtDesc =
		SortKey.For<Product>().ByDescending(p => p.CreatedAt, sql: "CreatedAt").ThenByUnique(p => p.Id, sql: "Id");

	public static readonly SortKeyRegistry<ProductSortField, Product> Ascending = new SortKeyRegistry<ProductSortField, Product>()
		.Add(ProductSortField.Id, IdAsc)
		.Add(ProductSortField.Name, NameAsc)
		.Add(ProductSortField.Price, PriceAsc)
		.Add(ProductSortField.CreatedAt, CreatedAtAsc);

	public static SortKey<Product> Resolve(ProductSortField field, SortDirection direction)
	{
		Ascending.EnsureComplete();
		return (field, direction) switch
		{
			(ProductSortField.Id, SortDirection.Descending) => IdDesc,
			(ProductSortField.Name, SortDirection.Descending) => NameDesc,
			(ProductSortField.Price, SortDirection.Descending) => PriceDesc,
			(ProductSortField.CreatedAt, SortDirection.Descending) => CreatedAtDesc,
			_ => Ascending.Get(field)
		};
	}
}
