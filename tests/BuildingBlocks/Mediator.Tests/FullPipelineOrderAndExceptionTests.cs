using System.Diagnostics;
using BuildingBlocks.Mediator.DependencyInjection;
using BuildingBlocks.Mediator.Telemetry;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

/// <summary>
/// End-to-end pipeline matrix: FluentValidation (host) + ordered manual behavior + optional UseTelemetry.
/// Telemetry is <strong>not</strong> a pipeline behavior — it wraps the whole Send.
/// </summary>
public sealed class FullPipelineOrderAndExceptionTests
{
	[Fact]
	public async Task Success_RunsValidation_ThenManual_ThenHandler_InOrder()
	{
		var log = new List<string>();
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		await using var sp = BuildFullPipeline(log, listenerSource: "BuildingBlocks.Mediator.FullPipeline");

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 2));

		Assert.Equal("sku", result.Product);
		Assert.Equal(
			new[]
			{
				"validation:before",
				"manual:before",
				"handler",
				"manual:after",
				"validation:after"
			},
			log);
		Assert.Single(activities);
		Assert.Equal(true, activities[0].GetTagItem("mediator.success"));
	}

	[Fact]
	public async Task ValidationFailure_ShortCircuits_NoManualOrHandler()
	{
		var log = new List<string>();
		await using var sp = BuildFullPipeline(log, enableTelemetry: false);

		var ex = await Assert.ThrowsAsync<ValidationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 0)));

		Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateOrder.Quantity));
		Assert.Equal(new[] { "validation:before", "validation:failed" }, log);
		Assert.DoesNotContain("manual:before", log);
		Assert.DoesNotContain("handler", log);
	}

	[Fact]
	public async Task HandlerException_Propagates_PostStepsStillRun_TelemetryRecordsFault()
	{
		var log = new List<string>();
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		await using var sp = BuildFullPipeline(
			log,
			listenerSource: "BuildingBlocks.Mediator.FullPipeline",
			handler: typeof(ThrowingCreateOrderHandler));

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 1)));

		Assert.Equal("boom-handler", ex.Message);
		Assert.Equal(
			new[]
			{
				"validation:before",
				"manual:before",
				"manual:after",
				"validation:after"
			},
			log);
		Assert.DoesNotContain("handler", log); // throwing handler does not log "handler"
		var activity = Assert.Single(activities);
		Assert.Equal(ActivityStatusCode.Error, activity.Status);
		Assert.Equal(false, activity.GetTagItem("mediator.success"));
		Assert.Equal("boom-handler", activity.GetTagItem("exception.message"));
	}

	[Fact]
	public async Task ManualBehavior_ThrowBeforeNext_SkipsHandler_ValidationAfterStillRuns()
	{
		var log = new List<string>();
		await using var sp = BuildFullPipeline(
			log,
			enableTelemetry: false,
			manualBehaviorType: typeof(ThrowBeforeNextOpenBehavior<,>));

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 1)));

		Assert.Equal(
			new[]
			{
				"validation:before",
				"manual:throw-before",
				"validation:after"
			},
			log);
		Assert.DoesNotContain("handler", log);
	}

	[Fact]
	public async Task ExplicitOrder_CanPlaceManualOutsideValidation()
	{
		var log = new List<string>();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(log);
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			// Manual outermost (0), validation inner (50)
			cfg.AddOpenBehavior(typeof(OrderedManualOpenBehavior<,>), order: 0);
			cfg.AddOpenBehavior(typeof(OrderedValidationOpenBehavior<,>), order: 50);
		});
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
		services.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveQuantityValidator>();

		await using var sp = services.BuildServiceProvider();
		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 2));

		Assert.Equal(
			new[]
			{
				"manual:before",
				"validation:before",
				"handler",
				"validation:after",
				"manual:after"
			},
			log);
	}

	[Fact]
	public async Task WithoutUseTelemetry_PipelineStillOrders_NoLibraryActivity()
	{
		var log = new List<string>();
		var activities = new List<Activity>();
		using var listener = CreateListener(activities, "BuildingBlocks.Mediator.FullPipeline");

		await using var sp = BuildFullPipeline(log, enableTelemetry: false);

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("sku", 1));

		Assert.Equal(
			new[]
			{
				"validation:before",
				"manual:before",
				"handler",
				"manual:after",
				"validation:after"
			},
			log);
		Assert.Empty(activities);
		Assert.Null(sp.GetService<MediatorSendTelemetry>());
	}

	private static ServiceProvider BuildFullPipeline(
		List<string> log,
		bool enableTelemetry = true,
		string listenerSource = "BuildingBlocks.Mediator.FullPipeline",
		Type? handler = null,
		Type? manualBehaviorType = null)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(log);
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.AddOpenBehavior(typeof(OrderedValidationOpenBehavior<,>), order: 0);
			cfg.AddOpenBehavior(manualBehaviorType ?? typeof(OrderedManualOpenBehavior<,>), order: 100);
			if (enableTelemetry)
			{
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = listenerSource;
					o.EnableLogging = false;
					o.RecordException = true;
				});
			}
		});

		services.AddTransient(typeof(ICommandHandler<CreateOrder, OrderResult>), handler ?? typeof(CreateOrderHandler));
		services.AddTransient<IValidator<CreateOrder>, CreateOrderMustHaveQuantityValidator>();
		return services.BuildServiceProvider();
	}

	private static ActivityListener CreateListener(List<Activity> sink, string sourceName = "BuildingBlocks.Mediator.FullPipeline")
	{
		var listener = new ActivityListener
		{
			ShouldListenTo = s => s.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = a => sink.Add(a)
		};
		ActivitySource.AddActivityListener(listener);
		return listener;
	}
}

/// <summary>Host-style FluentValidation behavior with before/after logging (try/finally).</summary>
public sealed class OrderedValidationOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly IEnumerable<IValidator<TRequest>> _validators;
	private readonly List<string> _log;

	public OrderedValidationOpenBehavior(IEnumerable<IValidator<TRequest>> validators, List<string> log)
	{
		_validators = validators;
		_log = log;
	}

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("validation:before");
		try
		{
			if (_validators.Any())
			{
				var context = new ValidationContext<TRequest>(request);
				var failures = (await Task.WhenAll(
						_validators.Select(v => v.ValidateAsync(context, cancellationToken)))
					.ConfigureAwait(false))
					.SelectMany(r => r.Errors)
					.Where(f => f is not null)
					.ToList();

				if (failures.Count > 0)
				{
					_log.Add("validation:failed");
					throw new ValidationException(failures);
				}
			}

			return await next(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			if (!_log.Contains("validation:failed"))
				_log.Add("validation:after");
		}
	}
}

public sealed class OrderedManualOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public OrderedManualOpenBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("manual:before");
		try
		{
			return await next(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_log.Add("manual:after");
		}
	}
}

public sealed class ThrowBeforeNextOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public ThrowBeforeNextOpenBehavior(List<string> log) => _log = log;

	public Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("manual:throw-before");
		throw new InvalidOperationException("boom-manual");
	}
}
