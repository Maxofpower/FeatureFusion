using Aspire.Hosting;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.AddSigNoz;

public sealed class SigNozPasswordPolicyTests
{
    [Theory]
    [InlineData(SigNozUiCredentials.DefaultPassword)]
    [InlineData("DevPassword123!")]
    [InlineData("Admin@Signoz1234")]
    public void Default_and_example_passwords_satisfy_signoz_policy(string password)
    {
        Assert.True(SigNozPasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData("DevPass123!")] // 11 chars — caused SigNoz "failed to validate config user"
    [InlineData("short1!A")]
    [InlineData("nouppercase1!")]
    [InlineData("NOLOWERCASE1!")]
    [InlineData("NoDigitsHere!!")]
    [InlineData("NoSymbolHere12")]
    [InlineData("HasBadChar;123")]
    public void Invalid_passwords_fail_policy(string password)
    {
        Assert.False(SigNozPasswordPolicy.IsValid(password));
    }

    [Fact]
    public void WithUi_rejects_password_that_signoz_would_reject()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        var ex = Assert.Throws<ArgumentException>(() => signoz.WithUi(adminPassword: "DevPass123!"));
        Assert.Equal("AdminPassword", ex.ParamName);
        Assert.Contains("12", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSigNoz_rejects_invalid_password_from_options()
    {
        var builder = DistributedApplication.CreateBuilder();

        var ex = Assert.Throws<ArgumentException>(() => builder.AddSigNoz("signoz", configure: o =>
        {
            o.UiCredentials.AdminPassword = "DevPass123!";
        }));

        Assert.Equal("AdminPassword", ex.ParamName);
    }
}
