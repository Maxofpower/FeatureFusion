using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EventBusRabbitMQ.Infrastructure;

public class EventBusSubscriptionInfo
{
    public ConcurrentDictionary<string, Type> EventTypes { get; } = [];

    public JsonSerializerOptions JsonSerializerOptions { get; } = new(DefaultSerializerOptions);

    internal static readonly JsonSerializerOptions DefaultSerializerOptions = new()
    {
        TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault ? CreateDefaultTypeResolver() : JsonTypeInfoResolver.Combine()
    };

	private static DefaultJsonTypeInfoResolver CreateDefaultTypeResolver()
	{
		return new DefaultJsonTypeInfoResolver();
	}
}
