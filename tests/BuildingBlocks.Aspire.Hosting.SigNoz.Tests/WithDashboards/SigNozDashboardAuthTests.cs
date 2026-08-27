using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Internal;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.WithDashboards;

public sealed class SigNozDashboardAuthTests
{
    [Fact]
    public void UserExistsInContext_requires_user_or_exists_flag()
    {
        using var withUser = JsonDocument.Parse("""{"user":{"id":"u1"},"orgs":[{"id":"o1"}]}""");
        using var withExists = JsonDocument.Parse("""{"exists":true,"orgs":[{"id":"o1"}]}""");
        using var orgOnly = JsonDocument.Parse("""{"orgs":[{"id":"o1"}]}""");

        Assert.True(SigNozDashboardSeeder.UserExistsInContextForTest(withUser.RootElement));
        Assert.True(SigNozDashboardSeeder.UserExistsInContextForTest(withExists.RootElement));
        Assert.False(SigNozDashboardSeeder.UserExistsInContextForTest(orgOnly.RootElement));
    }

    [Fact]
    public void Ui_store_suffix_is_stable_per_email()
    {
        var a = new SigNozUiCredentials { AdminEmail = "dev@local.test" };
        var b = new SigNozUiCredentials { AdminEmail = "DEV@local.test" };
        var c = new SigNozUiCredentials { AdminEmail = "admin@localhost.local" };

        Assert.Equal(SigNozUiStore.GetVolumeSuffix(a), SigNozUiStore.GetVolumeSuffix(b));
        Assert.NotEqual(SigNozUiStore.GetVolumeSuffix(a), SigNozUiStore.GetVolumeSuffix(c));
    }
}
