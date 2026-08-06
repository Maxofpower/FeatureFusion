using System;
using System.Threading.Tasks;
using Enyim.Caching;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Enyim.Caching.Configuration;
using Xunit;

public sealed class MemcachedFixture : IAsyncLifetime
{
	private readonly IContainer _memcachedContainer;
	private MemcachedClient _memcachedClient = null!;
	private readonly ILoggerFactory _loggerFactory;

	public MemcachedClient MemcachedClient => _memcachedClient;
	public ILogger<MemcachedCacheManager> Logger { get; private set; }

	public MemcachedFixture()
	{
		_loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
		Logger = _loggerFactory.CreateLogger<MemcachedCacheManager>();

		_memcachedContainer = new ContainerBuilder()
			.WithImage("memcached:latest")
			.WithPortBinding(11211, assignRandomHostPort: true)
			.WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(11211))
			.Build();
	}

	public async Task InitializeAsync()
	{
		await _memcachedContainer.StartAsync();
		var port = _memcachedContainer.GetMappedPublicPort(11211);

		var options = Options.Create(new MemcachedClientOptions
		{
			Servers = { new Server { Address = "localhost", Port = port } }
		});

		var config = new MemcachedClientConfiguration(
			_loggerFactory,
			options,
			configuration: null,
			transcoder: null,
			keyTransformer: null);

		_memcachedClient = new MemcachedClient(_loggerFactory, config);
	}

	public async Task DisposeAsync()
	{

		// Dispose the client properly to clean up resources after tests
		_memcachedClient?.Dispose();
	
	await _memcachedContainer.DisposeAsync();
	}

	public MemcachedCacheManager CreateCacheManager() =>
		new MemcachedCacheManager(_memcachedClient, Logger);

	public async Task ClearCacheAsync()
	{
		
		try
		{
			await _memcachedClient.FlushAllAsync();
		}
		catch 
		{
			// memcached disposed
			//throw new Exception("Failed to clear cache.", ex);
		}
	}
}