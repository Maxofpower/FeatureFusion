namespace Aspire.Hosting;

/// <summary>
/// Local-dev SigNoz UI admin credentials used for first-run registration and dashboard seeding.
/// </summary>
/// <remarks>
/// Configure via <see cref="SigNozBuilderExtensions.WithUi"/> or <see cref="SigNozOptions"/> on <c>AddSigNoz</c>.
/// </remarks>
public sealed class SigNozUiCredentials
{
    /// <summary>Default local-dev admin email.</summary>
    public const string DefaultEmail = "admin@localhost.local";

    /// <summary>Default local-dev admin password.</summary>
    public const string DefaultPassword = "Admin@Signoz1";

    /// <summary>Default display name for the first admin user.</summary>
    public const string DefaultAdminName = "Local Admin";

    /// <summary>Default organization name on first registration.</summary>
    public const string DefaultOrgName = "default";

    /// <summary>
    /// Gets or sets the admin email for SigNoz UI login and dashboard seeding.
    /// </summary>
    public string AdminEmail { get; set; } = DefaultEmail;

    /// <summary>
    /// Gets or sets the admin password for SigNoz UI login and dashboard seeding.
    /// </summary>
    /// <remarks>
    /// SigNoz root-user provisioning rejects passwords that do not meet the same rules as
    /// <c>types.IsPasswordValid</c>: at least 12 characters, one uppercase, one lowercase,
    /// one digit, and one symbol from the SigNoz whitelist (~!@#$%^&amp;* etc.; minimum 12 characters).
    /// </remarks>
    public string AdminPassword { get; set; } = DefaultPassword;

    /// <summary>
    /// Gets or sets the display name used when registering the first admin user.
    /// </summary>
    public string AdminName { get; set; } = DefaultAdminName;

    /// <summary>
    /// Gets or sets the organization name used when registering the first admin user.
    /// </summary>
    public string OrgName { get; set; } = DefaultOrgName;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AdminEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(AdminPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(AdminName);
        ArgumentException.ThrowIfNullOrWhiteSpace(OrgName);

        if (!SigNozPasswordPolicy.IsValid(AdminPassword))
        {
            throw new ArgumentException(SigNozPasswordPolicy.RequirementsMessage, nameof(AdminPassword));
        }
    }

    internal SigNozUiCredentials Clone() =>
        new()
        {
            AdminEmail = AdminEmail,
            AdminPassword = AdminPassword,
            AdminName = AdminName,
            OrgName = OrgName,
        };

    internal bool IsDefault() =>
        string.Equals(AdminEmail, DefaultEmail, StringComparison.Ordinal)
        && string.Equals(AdminPassword, DefaultPassword, StringComparison.Ordinal)
        && string.Equals(AdminName, DefaultAdminName, StringComparison.Ordinal)
        && string.Equals(OrgName, DefaultOrgName, StringComparison.Ordinal);
}
