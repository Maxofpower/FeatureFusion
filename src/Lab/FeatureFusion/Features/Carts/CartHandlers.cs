using BuildingBlocks.Mediator;
using FeatureFusion.Domain.Carts;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Carts;

public sealed class GetCartQueryHandler : IQueryHandler<GetCartQuery, Result<CartDto>>
{
	private readonly CatalogDbContext _db;
	private readonly TimeProvider _time;

	public GetCartQueryHandler(CatalogDbContext db, TimeProvider? time = null)
	{
		_db = db;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CartDto>> Handle(GetCartQuery request, CancellationToken cancellationToken)
	{
		var cart = await GetOrCreateCartAsync(request.CustomerId, cancellationToken).ConfigureAwait(false);
		if (cart is null)
			return Result<CartDto>.Failure("Customer not found.", StatusCodes.Status404NotFound);

		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return Result<CartDto>.Success(ToDto(cart));
	}

	private async Task<Cart?> GetOrCreateCartAsync(int customerId, CancellationToken cancellationToken)
	{
		var cart = await LoadCartAsync(customerId, cancellationToken).ConfigureAwait(false);
		if (cart is not null)
			return cart;

		var customerExists = await _db.Customers.AsNoTracking()
			.AnyAsync(c => (int)c.Id == customerId, cancellationToken)
			.ConfigureAwait(false);
		if (!customerExists)
			return null;

		var now = UtcNow();
		cart = Cart.Create(CustomerId.From(customerId), now);
		_db.Carts.Add(cart);
		return cart;
	}

	private Task<Cart?> LoadCartAsync(int customerId, CancellationToken cancellationToken) =>
		_db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == customerId, cancellationToken);

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}

	private static CartDto ToDto(Cart cart) =>
		new(
			(int)cart.CustomerId,
			cart.Items
				.OrderBy(i => (int)i.ProductId)
				.Select(i => new CartItemDto((int)i.ProductId, i.Quantity))
				.ToList());
}

public sealed class AddCartItemCommandHandler : ICommandHandler<AddCartItemCommand, Result<CartDto>>
{
	private readonly CatalogDbContext _db;
	private readonly TimeProvider _time;

	public AddCartItemCommandHandler(CatalogDbContext db, TimeProvider? time = null)
	{
		_db = db;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CartDto>> Handle(AddCartItemCommand request, CancellationToken cancellationToken)
	{
		var customerExists = await _db.Customers.AsNoTracking()
			.AnyAsync(c => (int)c.Id == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (!customerExists)
			return Result<CartDto>.Failure("Customer not found.", StatusCodes.Status404NotFound);

		var product = await _db.Product.AsNoTracking()
			.FirstOrDefaultAsync(p => (int)p.Id == request.ProductId, cancellationToken)
			.ConfigureAwait(false);
		if (product is null)
			return Result<CartDto>.Failure($"Product '{request.ProductId}' not found.", StatusCodes.Status404NotFound);
		if (!product.Published || product.Deleted)
			return Result<CartDto>.Failure(
				$"Product '{request.ProductId}' is not available for sale.",
				StatusCodes.Status409Conflict);

		var cart = await GetOrCreateCartAsync(request.CustomerId, cancellationToken).ConfigureAwait(false);
		cart.AddOrIncrement(ProductId.From(request.ProductId), request.Quantity, UtcNow());
		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return Result<CartDto>.Success(ToDto(cart));
	}

	private async Task<Cart> GetOrCreateCartAsync(int customerId, CancellationToken cancellationToken)
	{
		var cart = await _db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == customerId, cancellationToken)
			.ConfigureAwait(false);
		if (cart is not null)
			return cart;

		cart = Cart.Create(CustomerId.From(customerId), UtcNow());
		_db.Carts.Add(cart);
		return cart;
	}

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}

	private static CartDto ToDto(Cart cart) =>
		new(
			(int)cart.CustomerId,
			cart.Items
				.OrderBy(i => (int)i.ProductId)
				.Select(i => new CartItemDto((int)i.ProductId, i.Quantity))
				.ToList());
}

public sealed class UpdateCartItemQuantityCommandHandler
	: ICommandHandler<UpdateCartItemQuantityCommand, Result<CartDto>>
{
	private readonly CatalogDbContext _db;
	private readonly TimeProvider _time;

	public UpdateCartItemQuantityCommandHandler(CatalogDbContext db, TimeProvider? time = null)
	{
		_db = db;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CartDto>> Handle(
		UpdateCartItemQuantityCommand request,
		CancellationToken cancellationToken)
	{
		var cart = await _db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (cart is null)
			return Result<CartDto>.Failure("Cart not found.", StatusCodes.Status404NotFound);

		var productId = ProductId.From(request.ProductId);
		if (cart.Items.All(i => i.ProductId != productId))
			return Result<CartDto>.Failure("Cart item not found.", StatusCodes.Status404NotFound);

		cart.SetQuantity(productId, request.Quantity, UtcNow());
		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return Result<CartDto>.Success(ToDto(cart));
	}

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}

	private static CartDto ToDto(Cart cart) =>
		new(
			(int)cart.CustomerId,
			cart.Items
				.OrderBy(i => (int)i.ProductId)
				.Select(i => new CartItemDto((int)i.ProductId, i.Quantity))
				.ToList());
}

public sealed class RemoveCartItemCommandHandler : ICommandHandler<RemoveCartItemCommand, Result<CartDto>>
{
	private readonly CatalogDbContext _db;
	private readonly TimeProvider _time;

	public RemoveCartItemCommandHandler(CatalogDbContext db, TimeProvider? time = null)
	{
		_db = db;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CartDto>> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
	{
		var cart = await _db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (cart is null)
			return Result<CartDto>.Failure("Cart not found.", StatusCodes.Status404NotFound);

		cart.Remove(ProductId.From(request.ProductId), UtcNow());
		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return Result<CartDto>.Success(ToDto(cart));
	}

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}

	private static CartDto ToDto(Cart cart) =>
		new(
			(int)cart.CustomerId,
			cart.Items
				.OrderBy(i => (int)i.ProductId)
				.Select(i => new CartItemDto((int)i.ProductId, i.Quantity))
				.ToList());
}

public sealed class ClearCartCommandHandler : ICommandHandler<ClearCartCommand, Result<CartDto>>
{
	private readonly CatalogDbContext _db;
	private readonly TimeProvider _time;

	public ClearCartCommandHandler(CatalogDbContext db, TimeProvider? time = null)
	{
		_db = db;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CartDto>> Handle(ClearCartCommand request, CancellationToken cancellationToken)
	{
		var cart = await _db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (cart is null)
			return Result<CartDto>.Failure("Cart not found.", StatusCodes.Status404NotFound);

		cart.Clear(UtcNow());
		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return Result<CartDto>.Success(ToDto(cart));
	}

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}

	private static CartDto ToDto(Cart cart) =>
		new(
			(int)cart.CustomerId,
			cart.Items
				.OrderBy(i => (int)i.ProductId)
				.Select(i => new CartItemDto((int)i.ProductId, i.Quantity))
				.ToList());
}
