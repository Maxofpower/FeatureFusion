using Enyim.Caching;

public interface IMemcachedClientFactory
{
	IMemcachedClient CreateClient();
}

public class MemcachedClientFactory : IMemcachedClientFactory
{
	private readonly IServiceProvider _serviceProvider;

	public MemcachedClientFactory(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}

	public IMemcachedClient CreateClient()
	{
		return _serviceProvider.GetRequiredService<IMemcachedClient>();
	}
}