using System.Diagnostics;
using System.Diagnostics.Metrics;
using BuildingBlocks.Mediator.DependencyInjection;
using BuildingBlocks.Mediator.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mediator.Tests;

public sealed class TelemetryRegistrationTests
{
	[Fact]
	public async Task UseTelemetry_OnSuccess_CreatesActivityWithTags()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));

		var activity = Assert.Single(activities);
		Assert.Contains("CreateOrder", activity.OperationName);
		Assert.Equal("CreateOrder", activity.GetTagItem("mediator.request_name"));
		Assert.Equal(true, activity.GetTagItem("mediator.success"));
	}

	[Fact]
	public async Task UseTelemetry_OnFault_RecordsException_AndRethrows()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = "BuildingBlocks.Mediator.Tests";
					o.RecordException = true;
					o.EnableLogging = true;
				});
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, ThrowingCreateOrderHandler>());

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1)));
		Assert.Equal("boom-handler", ex.Message);

		var activity = Assert.Single(activities);
		Assert.Equal(ActivityStatusCode.Error, activity.Status);
		Assert.Equal(false, activity.GetTagItem("mediator.success"));
		Assert.Equal("boom-handler", activity.GetTagItem("exception.message"));
	}

	[Fact]
	public async Task UseTelemetry_Omitted_DoesNotCreateLibraryActivity()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities, sourceName: "BuildingBlocks.Mediator.Tests");

		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Empty(activities);
	}

	[Fact]
	public async Task UseTelemetry_EnableLoggingFalse_StillSucceeds()
	{
		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = "BuildingBlocks.Mediator.Tests";
					o.EnableLogging = false;
				});
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		var result = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Equal("A", result.Product);
	}

	[Fact]
	public async Task UseTelemetry_RecordExceptionFalse_StillRethrows()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = "BuildingBlocks.Mediator.Tests";
					o.RecordException = false;
				});
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, ThrowingCreateOrderHandler>());

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1)));

		var activity = Assert.Single(activities);
		Assert.Null(activity.GetTagItem("exception.message"));
		Assert.Equal(ActivityStatusCode.Error, activity.Status);
	}

	[Fact]
	public async Task UseTelemetry_OnQuery_SetsQueryKindTag()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
			},
			s => s.AddTransient<IQueryHandler<GetOrder, OrderResult>, GetOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new GetOrder(Guid.NewGuid()));

		var activity = Assert.Single(activities);
		Assert.Equal("query", activity.GetTagItem("mediator.message_kind"));
	}

	[Fact]
	public async Task UseTelemetry_OnVoidCommand_SetsVoidCommandKindTag()
	{
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);
		var state = new HandlerState();

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
			},
			s =>
			{
				s.AddSingleton(state);
				s.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
			});

		await sp.GetRequiredService<ISender>().Send(new CancelOrder(Guid.NewGuid()));

		var activity = Assert.Single(activities);
		Assert.Equal("void-command", activity.GetTagItem("mediator.message_kind"));
		Assert.True(state.VoidExecuted);
	}

	[Fact]
	public async Task UseTelemetry_WrapsPipelineAndHandler_ActivityVisibleInsideBehavior()
	{
		// Activity starts around Send — behaviors and handlers see Activity.Current.
		var log = new List<string>();
		var activities = new List<Activity>();
		using var listener = CreateListener(activities);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
				cfg.AddOpenBehavior(typeof(ActivityProbeOpenBehavior<,>));
			},
			s =>
			{
				s.AddSingleton(log);
				s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
			});

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));

		Assert.Contains("probe:enter:has-activity", log);
		Assert.Contains("probe:exit:has-activity", log);
		Assert.Contains("handler", log);
		Assert.Single(activities);
	}

	[Fact]
	public void UseTelemetry_DoesNotRegisterPipelineBehavior()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
			cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
		});
		services.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>();

		Assert.Contains(services, d => d.ServiceType == typeof(MediatorSendTelemetry));
		Assert.DoesNotContain(services, d =>
			d.ImplementationType is { IsGenericType: true } t
			&& t.Name.Contains("Telemetry", StringComparison.Ordinal)
			&& d.ServiceType.IsGenericType
			&& d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
	}

	[Fact]
	public async Task UseTelemetry_CustomActivitySourceName_IsHonored()
	{
		var activities = new List<Activity>();
		const string source = "BuildingBlocks.Mediator.CustomSource";
		using var listener = CreateListener(activities, source);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = source);
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Single(activities);
		Assert.Equal(source, Assert.Single(activities).Source.Name);
	}

	[Fact]
	public void UseTelemetry_EmptyActivitySourceName_Throws()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		var ex = Assert.Throws<ArgumentException>(() =>
			services.AddMediator(cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "  ");
			}));
		Assert.Contains("ActivitySourceName", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UseTelemetry_OnSuccess_RecordsDurationAndSendCounter()
	{
		using var metrics = new MetricSink("BuildingBlocks.Mediator.Tests");

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));

		var duration = Assert.Single(metrics.Doubles, d => d.Instrument == "mediator.send.duration");
		Assert.True(duration.Value >= 0);
		Assert.Equal("command", duration.Tags["mediator.message_kind"]);
		Assert.Equal("CreateOrder", duration.Tags["mediator.request_name"]);

		var count = Assert.Single(metrics.Longs, d => d.Instrument == "mediator.send");
		Assert.Equal(1L, count.Value);
		Assert.Equal(true, count.Tags["mediator.success"]);
		Assert.Equal("command", count.Tags["mediator.message_kind"]);
	}

	[Fact]
	public async Task UseTelemetry_OnFault_RecordsFailedSendCounter_AndRethrows()
	{
		using var metrics = new MetricSink("BuildingBlocks.Mediator.Tests");

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o => o.ActivitySourceName = "BuildingBlocks.Mediator.Tests");
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, ThrowingCreateOrderHandler>());

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1)));

		var count = Assert.Single(metrics.Longs, d => d.Instrument == "mediator.send");
		Assert.Equal(1L, count.Value);
		Assert.Equal(false, count.Tags["mediator.success"]);
		Assert.Single(metrics.Doubles, d => d.Instrument == "mediator.send.duration");
	}

	[Fact]
	public async Task UseTelemetry_EnableMetricsFalse_DoesNotRecord()
	{
		using var metrics = new MetricSink("BuildingBlocks.Mediator.Tests");

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = "BuildingBlocks.Mediator.Tests";
					o.EnableMetrics = false;
				});
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Empty(metrics.Doubles);
		Assert.Empty(metrics.Longs);
	}

	[Fact]
	public async Task UseTelemetry_Omitted_DoesNotRecordMetrics()
	{
		using var metrics = new MetricSink("BuildingBlocks.Mediator.Tests");

		await using var sp = TestHost.Build(s =>
			s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Empty(metrics.Doubles);
		Assert.Empty(metrics.Longs);
	}

	[Fact]
	public async Task UseTelemetry_CustomMeterName_IsHonored()
	{
		const string meterName = "BuildingBlocks.Mediator.CustomMeter";
		using var metrics = new MetricSink(meterName);

		using var sp = TestHost.BuildWithAddMediator(
			cfg =>
			{
				cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
				cfg.UseTelemetry(o =>
				{
					o.ActivitySourceName = "BuildingBlocks.Mediator.Tests";
					o.MeterName = meterName;
				});
			},
			s => s.AddTransient<ICommandHandler<CreateOrder, OrderResult>, CreateOrderHandler>());

		_ = await sp.GetRequiredService<ISender>().Send(new CreateOrder("A", 1));
		Assert.Single(metrics.Longs, d => d.Instrument == "mediator.send");
	}

	private static ActivityListener CreateListener(List<Activity> sink, string sourceName = "BuildingBlocks.Mediator.Tests")
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

	private sealed class MetricSink : IDisposable
	{
		private readonly MeterListener _listener;

		public List<(string Instrument, double Value, Dictionary<string, object?> Tags)> Doubles { get; } = new();

		public List<(string Instrument, long Value, Dictionary<string, object?> Tags)> Longs { get; } = new();

		public MetricSink(string meterName)
		{
			_listener = new MeterListener();
			_listener.InstrumentPublished = (instrument, listener) =>
			{
				if (instrument.Meter.Name == meterName)
					listener.EnableMeasurementEvents(instrument);
			};
			_listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
				Doubles.Add((instrument.Name, value, ToDictionary(tags))));
			_listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
				Longs.Add((instrument.Name, value, ToDictionary(tags))));
			_listener.Start();
		}

		public void Dispose() => _listener.Dispose();

		private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
		{
			var map = new Dictionary<string, object?>(StringComparer.Ordinal);
			foreach (var tag in tags)
				map[tag.Key] = tag.Value;
			return map;
		}
	}
}
