namespace BuildingBlocks.Pagination.TestSupport;

public sealed class Money
{
	public decimal Amount { get; set; }
	public string Currency { get; set; } = "USD";
}

public readonly record struct ProductId(int Value);

public sealed class TypedRow
{
	public int Id { get; set; }
	public bool Flag { get; set; }
	public bool? OptionalFlag { get; set; }
	public ItemKind Kind { get; set; }
	public DateOnly Day { get; set; }
	public TimeOnly Clock { get; set; }
	public TimeSpan Duration { get; set; }
	public short ShortId { get; set; }
	public byte Tiny { get; set; }
	public float Ratio { get; set; }
	public ItemKind? OptionalKind { get; set; }
	public Money Price { get; set; } = new();
	public ProductId TypedId { get; set; }
	public byte[] Payload { get; set; } = [];
}
