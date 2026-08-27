using BuildingBlocks.Mediator.DependencyInjection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

/// <summary>
/// Host ValidationBehavior resilience matrix (exact IValidator&lt;TRequest&gt; only).
/// Uses a test-local behavior matching FeatureFusion's host ValidationBehavior.
/// </summary>
public sealed class ValidationPipelineTests
{
	[Fact]
	public async Task NoValidator_SkipsAndHandlerRuns()
	{
		await using var sp = Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("ok", 1));
		Assert.Equal("ok", result.Product);
	}

	[Fact]
	public async Task SingleValidator_Failure_ThrowsAndHandlerNotCalled()
	{
		var log = new List<string>();
		await using var sp = Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveQuantityValidator>();
		});

		var ex = await Assert.ThrowsAsync<ValidationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 0)));

		Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateOrder.Quantity));
		Assert.DoesNotContain("handler", log);
	}

	[Fact]
	public async Task SingleValidator_Success_HandlerRuns()
	{
		var log = new List<string>();
		await using var sp = Build(s =>
		{
			s.AddSingleton(log);
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveQuantityValidator>();
		});

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 2));
		Assert.Equal("x", result.Product);
		Assert.Contains("handler", log);
	}

	[Fact]
	public async Task AbstractValidatorStyle_Failure_Throws()
	{
		await using var sp = Build(s =>
		{
			s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>();
			s.AddTransient<IValidator<GetOrder>, GetOrderAbstractStyleValidator>();
		});

		await Assert.ThrowsAsync<ValidationException>(
			() => sp.GetRequiredService<ISender>().Send(new GetOrder(Guid.Empty)));
	}

	[Fact]
	public async Task TwoValidators_AggregateFailures()
	{
		await using var sp = Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveQuantityValidator>();
			s.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveProductValidator>();
		});

		var ex = await Assert.ThrowsAsync<ValidationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("", 0)));

		Assert.True(ex.Errors.Count() >= 2);
		Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateOrder.Quantity));
		Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateOrder.Product));
	}

	[Fact]
	public async Task DtoValidator_DoesNotAffectUnrelatedCommand()
	{
		await using var sp = Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			s.AddTransient<IValidator<UnrelatedDto>, UnrelatedDtoValidator>();
		});

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("ok", 1));
		Assert.Equal("ok", result.Product);
	}

	[Fact]
	public async Task NonGenericIValidatorOnly_SkipsPipelineValidation()
	{
		await using var sp = Build(s =>
		{
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			// Registered only as non-generic IValidator — host ValidationBehavior ignores it.
			s.AddSingleton<IValidator>(new CreateOrderMustHaveQuantityValidator());
		});

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("x", 0));
		Assert.Equal(0, result.Quantity);
	}

	[Fact]
	public async Task VoidCommand_WithValidator_ThrowsOnFailure()
	{
		var state = new HandlerState();
		await using var sp = Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			s.AddTransient<IValidator<CancelOrder>, CancelOrderValidator>();
		});

		await Assert.ThrowsAsync<ValidationException>(
			() => sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.Empty)));
		Assert.False(state.VoidExecuted);
	}

	[Fact]
	public async Task VoidCommand_WithValidator_SucceedsWhenValid()
	{
		var state = new HandlerState();
		var id = Guid.NewGuid();
		await using var sp = Build(s =>
		{
			s.AddSingleton(state);
			s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			s.AddTransient<IValidator<CancelOrder>, CancelOrderValidator>();
		});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(id));
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public void ValidationException_ErrorGrouping_MatchesProblemDetailsShape()
	{
		var ex = new ValidationException(new[]
		{
			new ValidationFailure("Quantity", "must be > 0"),
			new ValidationFailure("Quantity", "must be integer"),
			new ValidationFailure("Product", "required")
		});

		var errors = ex.Errors
			.GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? string.Empty : e.PropertyName)
			.ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

		Assert.Equal(2, errors["Quantity"].Length);
		Assert.Single(errors["Product"]);
	}

	private static ServiceProvider Build(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.AddOpenBehavior(typeof(TestValidationBehavior<,>));
		});
		configure(services);
		return services.BuildServiceProvider();
	}

	/// <summary>Mirrors FeatureFusion ValidationBehavior (host pattern).</summary>
	private sealed class TestValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
		where TRequest : notnull
	{
		private readonly IEnumerable<IValidator<TRequest>> _validators;

		public TestValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
			=> _validators = validators;

		public async Task<TResponse> Handle(
			TRequest request,
			RequestHandlerDelegate<TResponse> next,
			CancellationToken cancellationToken = default)
		{
			if (!_validators.Any())
				return await next(cancellationToken);

			var context = new ValidationContext<TRequest>(request);
			var failures = (await Task.WhenAll(
					_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
				.SelectMany(r => r.Errors)
				.Where(f => f is not null)
				.ToList();

			if (failures.Count > 0)
				throw new ValidationException(failures);

			return await next(cancellationToken);
		}
	}
}

public sealed class CreateOrderMustHaveQuantityValidator : AbstractValidator<CreateOrder>
{
	public CreateOrderMustHaveQuantityValidator()
	{
		RuleFor(x => x.Quantity).GreaterThan(0);
	}
}

public sealed class CreateOrderMustHaveProductValidator : AbstractValidator<CreateOrder>
{
	public CreateOrderMustHaveProductValidator()
	{
		RuleFor(x => x.Product).NotEmpty();
	}
}

public sealed class GetOrderAbstractStyleValidator : AbstractValidator<GetOrder>
{
	public GetOrderAbstractStyleValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
	}
}

public sealed record UnrelatedDto(string Name);

public sealed class UnrelatedDtoValidator : AbstractValidator<UnrelatedDto>
{
	public UnrelatedDtoValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class CancelOrderValidator : AbstractValidator<CancelOrder>
{
	public CancelOrderValidator() => RuleFor(x => x.Id).NotEmpty();
}
