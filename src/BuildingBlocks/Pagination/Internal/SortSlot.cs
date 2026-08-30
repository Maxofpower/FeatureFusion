namespace BuildingBlocks.Pagination;

internal enum SortSlotKind
{
	Expression = 0,
	Shadow = 1
}

internal sealed class SortSlot
{
	public required SortSlotKind Kind { get; init; }
	public required SortDirection Direction { get; init; }
	public required Type ClrType { get; init; }
	public required Type DeclaredType { get; init; }
	public required string FingerprintPart { get; init; }
	public required bool IsUnique { get; init; }
	public string? SqlIdentifier { get; init; }
	public LambdaExpression? Accessor { get; init; }
	public string? ShadowName { get; init; }
}
