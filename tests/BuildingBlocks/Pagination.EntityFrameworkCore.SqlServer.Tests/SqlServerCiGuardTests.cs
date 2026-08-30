using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.SqlServer.Tests;

public sealed class SqlServerCiGuardTests
{
	[Fact]
	public void Ci_Requires_SqlServer_QueryHint_Tests_To_Execute()
	{
		if (!SqlServerHintFixture.IsRequiredEnvironment)
		{
			return;
		}

		Assert.True(
			SqlServerHintFixture.CanRun,
			"SQL Server QueryHint CI requires Docker so Aspire can start mcr.microsoft.com/mssql/server:2022-latest. Skipped tests are a CI failure.");
	}
}
