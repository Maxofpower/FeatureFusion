using ModelContextProtocol.Client;

namespace IntegrationTests.Infrastructure.Mcp;

public static class LabMcpClient
{
	public static async Task<McpClient> CreateAsync(HttpClient http)
	{
		var endpoint = new Uri(http.BaseAddress ?? new Uri("http://localhost"), "mcp");
		var transport = new HttpClientTransport(
			new HttpClientTransportOptions { Endpoint = endpoint },
			http,
			ownsHttpClient: false);
		return await McpClient.CreateAsync(transport);
	}
}
