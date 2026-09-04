namespace BuildingBlocks.Pagination.TestSupport;

public enum ItemKind
{
	A = 0,
	B = 1,
	C = 2
}

public enum ItemSortField
{
	Id,
	Name,
	Price,
	CreatedAt
}

public sealed class Vendor
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
}

public sealed class CatalogItem
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	/// <summary>Double so EF Core Sqlite can ORDER BY (decimal is unsupported on that provider).</summary>
	public double Price { get; set; }
	public DateTime CreatedAt { get; set; }
	public ItemKind Kind { get; set; }
	public Guid ExternalId { get; set; }
	public long LongId { get; set; }
	public DateTime? OptionalAt { get; set; }
	public int VendorId { get; set; }
	/// <summary>Extra value-type slot for 8–9 column keyset tests.</summary>
	public byte Flag { get; set; }
	/// <summary>Extra value-type slot for 8–9 column keyset tests.</summary>
	public short Rank { get; set; }
	public Vendor? Vendor { get; set; }
}

public sealed class CatalogItemDto
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public double Price { get; set; }
}

public sealed class CatalogItemDtoMissingId
{
	public string Name { get; set; } = "";
	public double Price { get; set; }
}

public sealed class DecimalItem
{
	public int Id { get; set; }
	public decimal Amount { get; set; }
}

public sealed class OffsetItem
{
	public int Id { get; set; }
	public DateTimeOffset At { get; set; }
}
