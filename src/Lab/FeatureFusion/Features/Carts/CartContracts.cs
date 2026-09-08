using BuildingBlocks.Mediator;

namespace FeatureFusion.Features.Carts;

/// <summary>Cart line — quantity only; catalog price is resolved at checkout.</summary>
public sealed record CartItemDto(int ProductId, int Quantity);

/// <summary>Customer cart projection.</summary>
public sealed record CartDto(int CustomerId, IReadOnlyList<CartItemDto> Items);

/// <summary>HTTP body for adding a cart line.</summary>
public sealed record AddCartItemRequest(int ProductId, int Quantity);

/// <summary>HTTP body for setting a cart line quantity.</summary>
public sealed record UpdateCartItemQuantityRequest(int Quantity);

/// <summary>Get-or-create cart for a customer.</summary>
public sealed record GetCartQuery(int CustomerId) : IQuery<Result<CartDto>>;

/// <summary>Add or increment a product line on the customer's cart.</summary>
public sealed record AddCartItemCommand(int CustomerId, int ProductId, int Quantity)
	: ICommand<Result<CartDto>>;

/// <summary>Set quantity for a cart line (quantity ≤ 0 removes the line).</summary>
public sealed record UpdateCartItemQuantityCommand(int CustomerId, int ProductId, int Quantity)
	: ICommand<Result<CartDto>>;

/// <summary>Remove a product line from the cart.</summary>
public sealed record RemoveCartItemCommand(int CustomerId, int ProductId)
	: ICommand<Result<CartDto>>;

/// <summary>Clear all lines from the customer's cart.</summary>
public sealed record ClearCartCommand(int CustomerId) : ICommand<Result<CartDto>>;
