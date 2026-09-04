using System.Reflection;

namespace IntegrationTests.Infrastructure.Reporting;

public static class LabRunInfo
{
	public static string ReadGitSha()
	{
		var info = typeof(LabRunInfo).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		return string.IsNullOrWhiteSpace(info) ? "unknown" : info;
	}
}
