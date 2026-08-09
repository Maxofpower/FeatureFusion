using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.Benchmarks;

public static class Program
{
	public static void Main(string[] args)
		=> BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

public sealed record BenchCommand(int Value) : ICommand<int>;

public sealed class BenchHandler : ICommandHandler<BenchCommand, int>
{
	public Task<int> Handle(BenchCommand command, CancellationToken cancellationToken)
		=> Task.FromResult(command.Value);
}

public sealed class PassthroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	public Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
		=> next(cancellationToken);
}

[MemoryDiagnoser]
public class SendBenchmarks
{
	private ISender _noPipeline = null!;
	private ISender _oneBehavior = null!;
	private ISender _threeBehaviors = null!;
	private ServiceProvider _sp0 = null!;
	private ServiceProvider _sp1 = null!;
	private ServiceProvider _sp3 = null!;

	[GlobalSetup]
	public void Setup()
	{
		_sp0 = Build(behaviors: 0);
		_sp1 = Build(behaviors: 1);
		_sp3 = Build(behaviors: 3);
		_noPipeline = _sp0.GetRequiredService<ISender>();
		_oneBehavior = _sp1.GetRequiredService<ISender>();
		_threeBehaviors = _sp3.GetRequiredService<ISender>();

		// Warm wrapper caches
		_ = _noPipeline.Send(new BenchCommand(1)).GetAwaiter().GetResult();
		_ = _oneBehavior.Send(new BenchCommand(1)).GetAwaiter().GetResult();
		_ = _threeBehaviors.Send(new BenchCommand(1)).GetAwaiter().GetResult();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_sp0.Dispose();
		_sp1.Dispose();
		_sp3.Dispose();
	}

	[Benchmark(Baseline = true)]
	public Task<int> Send_NoPipeline()
		=> _noPipeline.Send(new BenchCommand(42));

	[Benchmark]
	public Task<int> Send_OneBehavior()
		=> _oneBehavior.Send(new BenchCommand(42));

	[Benchmark]
	public Task<int> Send_ThreeBehaviors()
		=> _threeBehaviors.Send(new BenchCommand(42));

	private static ServiceProvider Build(int behaviors)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(cfg =>
		{
			cfg.RegisterServicesFromAssemblyContaining<BenchHandler>();
			for (var i = 0; i < behaviors; i++)
				cfg.AddOpenBehavior(typeof(PassthroughBehavior<,>), order: i);
		});
		return services.BuildServiceProvider();
	}
}
