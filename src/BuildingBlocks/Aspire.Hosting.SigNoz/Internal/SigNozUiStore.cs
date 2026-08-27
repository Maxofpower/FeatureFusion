using System.Security.Cryptography;
using System.Text;

namespace Aspire.Hosting.Internal;

/// <summary>
/// Maps UI admin credentials to a dedicated sqlite volume suffix (local-dev only).
/// </summary>
internal static class SigNozUiStore
{
    internal const string MountPath = "/var/lib/signoz";

    internal static string GetVolumeSuffix(SigNozUiCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var normalizedEmail = credentials.AdminEmail.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
