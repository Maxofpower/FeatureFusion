namespace BuildingBlocks.Pagination.TestSupport;

public static class CatalogSeed
{
	public static readonly DateTime T0 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public static IReadOnlyList<CatalogItem> Items { get; } = Create();

	public static SortKey<CatalogItem> ById { get; } =
		SortKey.For<CatalogItem>().ThenByUnique(x => x.Id, sql: "Id");

	public static SortKey<CatalogItem> ByName { get; } =
		SortKey.For<CatalogItem>().By(x => x.Name, sql: "Name").ThenByUnique(x => x.Id, sql: "Id");

	public static SortKey<CatalogItem> ByPrice { get; } =
		SortKey.For<CatalogItem>().By(x => x.Price, sql: "Price").ThenByUnique(x => x.Id, sql: "Id");

	public static SortKey<CatalogItem> ByPriceDesc { get; } =
		SortKey.For<CatalogItem>().ByDescending(x => x.Price, sql: "Price").ThenByUnique(x => x.Id, sql: "Id");

	public static SortKey<CatalogItem> ByCreatedAt { get; } =
		SortKey.For<CatalogItem>().By(x => x.CreatedAt, sql: "CreatedAt").ThenByUnique(x => x.Id, sql: "Id");

	public static SortKey<CatalogItem> ByCreatedDescId { get; } =
		SortKey.For<CatalogItem>().ByDescending(x => x.CreatedAt, sql: "CreatedAt").ThenByUnique(x => x.Id, sql: "Id");

	public static SortKeyRegistry<ItemSortField, CatalogItem> Registry { get; } = new SortKeyRegistry<ItemSortField, CatalogItem>()
		.Add(ItemSortField.Id, ById)
		.Add(ItemSortField.Name, ByName)
		.Add(ItemSortField.Price, ByPrice)
		.Add(ItemSortField.CreatedAt, ByCreatedAt);

	public static SortKey<CatalogItem> For(ItemSortField field, SortDirection direction)
	{
		if (field == ItemSortField.Price && direction == SortDirection.Descending)
		{
			return ByPriceDesc;
		}

		if (field == ItemSortField.CreatedAt && direction == SortDirection.Descending)
		{
			return ByCreatedDescId;
		}

		if (direction == SortDirection.Descending && field == ItemSortField.Id)
		{
			return SortKey.For<CatalogItem>().ThenByUniqueDescending(x => x.Id, sql: "Id");
		}

		if (direction == SortDirection.Descending && field == ItemSortField.Name)
		{
			return SortKey.For<CatalogItem>().ByDescending(x => x.Name, sql: "Name").ThenByUnique(x => x.Id, sql: "Id");
		}

		return Registry.Get(field);
	}

	private static List<CatalogItem> Create()
	{
		var items = new List<CatalogItem>();
		for (var i = 1; i <= 12; i++)
		{
			items.Add(new CatalogItem
			{
				Id = i,
				Name = "Item-" + (char)('A' + ((i - 1) % 12)),
				Price = 10d + (i % 4) * 5d, // duplicates: 10,15,20,25 cycling
				CreatedAt = T0.AddDays(i),
				Kind = (ItemKind)(i % 3),
				ExternalId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
				LongId = 1000 + i,
				OptionalAt = i % 5 == 0 ? null : T0.AddHours(i),
				VendorId = (i % 3) + 1,
				Vendor = new Vendor { Id = (i % 3) + 1, Name = "Vendor-" + (char)('X' + (i % 3)) }
			});
		}

		return items;
	}
}
