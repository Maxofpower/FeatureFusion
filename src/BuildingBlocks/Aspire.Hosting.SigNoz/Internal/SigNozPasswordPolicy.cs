namespace Aspire.Hosting;

/// <summary>
/// Mirrors SigNoz v0.136 <c>types.IsPasswordValid</c> for root-user provisioning.
/// </summary>
internal static class SigNozPasswordPolicy
{
    /// <summary>Minimum password length enforced by SigNoz.</summary>
    public const int MinLength = 12;

    /// <summary>Symbol whitelist from SigNoz <c>pkg/types/factor_password.go</c>.</summary>
    private const string AllowedSymbols = "~!@#$%^&*()_+`-={}|[]\\:\"<>?,./";

    public static bool IsValid(string password)
    {
        if (password.Length < MinLength)
        {
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasNumber = false;
        var hasSymbol = false;

        foreach (var ch in password)
        {
            if (char.IsLower(ch))
            {
                hasLower = true;
            }
            else if (char.IsUpper(ch))
            {
                hasUpper = true;
            }
            else if (char.IsDigit(ch))
            {
                hasNumber = true;
            }
            else if (AllowedSymbols.Contains(ch))
            {
                hasSymbol = true;
            }
            else
            {
                return false;
            }
        }

        return hasUpper && hasLower && hasNumber && hasSymbol;
    }

    public static string RequirementsMessage { get; } =
        $"Password must be at least {MinLength} characters and include uppercase, lowercase, a digit, and a symbol from: {AllowedSymbols}";
}
