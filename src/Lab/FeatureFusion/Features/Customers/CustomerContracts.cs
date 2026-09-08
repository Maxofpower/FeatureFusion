using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using FluentValidation;

namespace FeatureFusion.Features.Customers;

/// <summary>Customer list card.</summary>
public sealed record CustomerListItemDto(
	int Id,
	string Email,
	string DisplayName,
	DateTime CreatedAt);

/// <summary>Customer detail.</summary>
public sealed record CustomerDetailDto(
	int Id,
	string Email,
	string DisplayName,
	DateTime CreatedAt);

/// <summary>Order summary on a customer orders page.</summary>
public sealed record CustomerOrderListItemDto(
	int Id,
	string OrderNumber,
	string Status,
	decimal Total,
	string Currency,
	DateTime CreatedAt,
	int LineCount);

/// <summary>OFFSET page of customer orders.</summary>
public sealed record CustomerOrderListDto(
	IReadOnlyList<CustomerOrderListItemDto> Items,
	int Page,
	int PageSize,
	int TotalCount);

/// <summary>Keyset list customers (BuildingBlocks.Pagination showcase).</summary>
[McpTool("customers.list", Description = "List customers (keyset pagination)")]
public sealed record ListCustomersQuery : IQuery<Result<PagedResult<CustomerListItemDto>>>
{
	/// <summary>Page size (1–50). Default 20.</summary>
	public int Limit { get; init; } = 20;

	/// <summary>Opaque cursor from a previous response.</summary>
	public string Cursor { get; init; } = string.Empty;
}

/// <summary>Customer detail by id.</summary>
[McpTool("customers.get", Description = "Get a customer by id")]
public sealed record GetCustomerQuery(int Id) : IQuery<Result<CustomerDetailDto>>;

/// <summary>Orders for a customer (OFFSET storefront-style paging).</summary>
public sealed record ListCustomerOrdersQuery : IQuery<Result<CustomerOrderListDto>>
{
	public required int CustomerId { get; init; }
	public int Page { get; init; } = 1;
	public int PageSize { get; init; } = 20;
}

public sealed class GetCustomerQueryValidator : AbstractValidator<GetCustomerQuery>
{
	public GetCustomerQueryValidator() =>
		RuleFor(x => x.Id).GreaterThan(0).WithMessage("Customer id is required.");
}

public sealed class ListCustomerOrdersQueryValidator : AbstractValidator<ListCustomerOrdersQuery>
{
	public ListCustomerOrdersQueryValidator() =>
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
}
