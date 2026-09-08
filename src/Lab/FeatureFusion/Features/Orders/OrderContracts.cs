using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using FluentValidation;

namespace FeatureFusion.Features.Orders;

/// <summary>Order list row.</summary>
public sealed record OrderListItemDto(
	int Id,
	string OrderNumber,
	int CustomerId,
	string Status,
	decimal Total,
	string Currency,
	DateTime CreatedAt,
	int LineCount);

/// <summary>Order line on detail.</summary>
public sealed record OrderLineDto(
	int ProductId,
	string? ProductName,
	string? ProductSlug,
	string? ProductSku,
	int Quantity,
	decimal UnitPrice,
	decimal LineTotal);

/// <summary>Order detail with lines.</summary>
public sealed record OrderDetailDto(
	int Id,
	string OrderNumber,
	int CustomerId,
	string? CustomerEmail,
	string? CustomerDisplayName,
	string Status,
	decimal Subtotal,
	decimal TaxAmount,
	decimal ShippingAmount,
	decimal Total,
	string Currency,
	DateTime CreatedAt,
	IReadOnlyList<OrderLineDto> Lines);

/// <summary>Keyset list orders (BuildingBlocks.Pagination).</summary>
[McpTool("orders.list", Description = "List orders (keyset pagination)")]
public sealed record ListOrdersQuery : IQuery<Result<PagedResult<OrderListItemDto>>>
{
	public int Limit { get; init; } = 20;
	public string Cursor { get; init; } = string.Empty;
}

/// <summary>Order detail by persistence id.</summary>
[McpTool("orders.get", Description = "Get an order by id")]
public sealed record GetOrderQuery(int Id) : IQuery<Result<OrderDetailDto>>;

public sealed class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
{
	public GetOrderQueryValidator() =>
		RuleFor(x => x.Id).GreaterThan(0).WithMessage("Order id is required.");
}
