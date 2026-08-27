namespace FeatureFusion.AppHost;

/// <summary>
/// Workarounds for Aspire DCP failing to autodetect Docker Desktop on Windows.
/// See https://github.com/dotnet/aspire/issues/7802
/// </summary>
internal static class DockerRuntime
{
	private const string DockerBin = @"C:\Program Files\Docker\Docker\resources\bin";
	private const string DockerDesktopPipe = "npipe:////./pipe/dockerDesktopLinuxEngine";

	/// <summary>
	/// Forces the container runtime to Docker and returns AppHost args with
	/// <c>--dcp-container-runtime docker</c> when missing.
	/// </summary>
	public static string[] Configure(string[] args)
	{
		Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", "docker");
		Environment.SetEnvironmentVariable("DOTNET_ASPIRE_CONTAINER_RUNTIME", "docker");

		if (OperatingSystem.IsWindows() && Directory.Exists(DockerBin))
		{
			var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			if (!path.Contains(DockerBin, StringComparison.OrdinalIgnoreCase))
			{
				Environment.SetEnvironmentVariable("PATH", DockerBin + Path.PathSeparator + path);
			}
		}

		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
		{
			Environment.SetEnvironmentVariable("DOCKER_HOST", DockerDesktopPipe);
		}

		if (args.Contains("--dcp-container-runtime", StringComparer.OrdinalIgnoreCase))
		{
			return args;
		}

		return [.. args, "--dcp-container-runtime", "docker"];
	}
}
