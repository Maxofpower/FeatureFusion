using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Internal;

/// <summary>
/// Seeds SigNoz dashboards after the UI is ready (local-dev only).
/// </summary>
internal static class SigNozDashboardSeeder
{
    private static readonly string[] EmbeddedDashboardResources =
    [
        "BuildingBlocks.Aspire.Hosting.SigNoz.buildingblocks-telemetry-dashboard.json",
        "BuildingBlocks.Aspire.Hosting.SigNoz.aspnetcore-otlp-v1.json",
    ];

    public static async Task SeedAsync(
        Uri baseAddress,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();

        using var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) };

        await WaitForHealthAsync(http, cancellationToken).ConfigureAwait(false);
        var token = await EnsureAuthenticatedAsync(http, credentials, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                $"SigNoz dashboard seed skipped: could not authenticate '{credentials.AdminEmail}'. " +
                "If you changed adminEmail, restart AppHost so a fresh sqlite volume is provisioned for that email.");
            return;
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var existing = await ListDashboardsAsync(http, cancellationToken).ConfigureAwait(false);

        foreach (var resourceName in EmbeddedDashboardResources)
        {
            var json = ReadEmbedded(resourceName);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            SigNozDashboardJson.Validate(json, resourceName);

            using var doc = JsonDocument.Parse(json);
            var title = TryGetTitle(doc.RootElement);
            if (title is not null
                && existing.TryGetValue(title, out var listed)
                && !await ShouldReplaceAsync(http, listed, doc.RootElement, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("/api/v2/dashboards", content, cancellationToken)
                .ConfigureAwait(false);
            // Idempotent best-effort: ignore conflicts / schema drift in local-dev.
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine(
                    $"SigNoz dashboard seed failed for '{title ?? resourceName}': {(int)response.StatusCode} {body}");
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when every listed copy is stale (or missing) and a new POST should run.
    /// Current copies are kept; extras and stale definitions are deleted.
    /// </summary>
    private static async Task<bool> ShouldReplaceAsync(
        HttpClient http,
        IReadOnlyList<ListedDashboard> listed,
        JsonElement embeddedRoot,
        CancellationToken cancellationToken)
    {
        var keepCurrent = false;
        foreach (var dashboard in listed)
        {
            if (string.IsNullOrWhiteSpace(dashboard.Id))
            {
                continue;
            }

            var current = await IsCurrentAsync(http, dashboard, embeddedRoot, cancellationToken)
                .ConfigureAwait(false);
            if (current && !keepCurrent)
            {
                keepCurrent = true;
                continue;
            }

            await TryDeleteDashboardAsync(http, dashboard, cancellationToken).ConfigureAwait(false);
        }

        return !keepCurrent;
    }

    private static async Task<bool> IsCurrentAsync(
        HttpClient http,
        ListedDashboard listed,
        JsonElement embeddedRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"/api/v2/dashboards/{listed.Id}", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return !NeedsReplace(doc.RootElement, embeddedRoot);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task TryDeleteDashboardAsync(
        HttpClient http,
        ListedDashboard listed,
        CancellationToken cancellationToken)
    {
        try
        {
            using var delete = await http.DeleteAsync($"/api/v2/dashboards/{listed.Id}", cancellationToken)
                .ConfigureAwait(false);
            if (!delete.IsSuccessStatusCode)
            {
                var body = await delete.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine(
                    $"SigNoz dashboard delete failed for '{listed.Title}' ({listed.Id}): {(int)delete.StatusCode} {body}");
            }
        }
        catch (HttpRequestException)
        {
            // Best-effort local-dev cleanup.
        }
    }

    private static async Task WaitForHealthAsync(HttpClient http, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync("/api/v1/health", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // UI still booting
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // timeout per attempt
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string?> EnsureAuthenticatedAsync(
        HttpClient http,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        var loginToken = await TryLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(loginToken))
        {
            return loginToken;
        }

        await TryRegisterAsync(http, credentials, cancellationToken).ConfigureAwait(false);
        return await TryLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryRegisterAsync(
        HttpClient http,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            email = credentials.AdminEmail,
            password = credentials.AdminPassword,
            name = credentials.AdminName,
            orgName = credentials.OrgName,
        };

        try
        {
            using var response = await http.PostAsJsonAsync("/api/v1/register", payload, cancellationToken)
                .ConfigureAwait(false);
            _ = response;
        }
        catch (HttpRequestException)
        {
            // Already registered or register disabled
        }
    }

    private static async Task<string?> TryLoginAsync(
        HttpClient http,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        // SigNoz ≥0.110: /api/v1/login is SPA-routed. Use sessions context + email_password.
        try
        {
            var orgId = await TryResolveOrgIdAsync(http, credentials, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(orgId))
            {
                return await TryLegacyLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
            }

            var payload = new
            {
                email = credentials.AdminEmail,
                password = credentials.AdminPassword,
                orgId,
            };

            using var response = await http.PostAsJsonAsync("/api/v2/sessions/email_password", payload, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await LogLoginFailureAsync(response, credentials, cancellationToken).ConfigureAwait(false);
                return await TryLegacyLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return TryReadAccessToken(doc.RootElement);
        }
        catch (HttpRequestException)
        {
            return await TryLegacyLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return await TryLegacyLoginAsync(http, credentials, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task LogLoginFailureAsync(
        HttpResponseMessage response,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (body.Contains("user_not_found", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"SigNoz login failed for '{credentials.AdminEmail}': user_not_found in the configured organization. " +
                "Restart AppHost after changing adminEmail so SigNoz provisions a fresh sqlite store for that email.");
        }
    }

    private static async Task<string?> TryResolveOrgIdAsync(
        HttpClient http,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
                $"/api/v2/sessions/context?email={Uri.EscapeDataString(credentials.AdminEmail)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        // Do not pick an org when the user does not exist yet — otherwise email_password returns user_not_found.
        if (!UserExistsInContext(data))
        {
            return null;
        }

        if (!data.TryGetProperty("orgs", out var orgs) || orgs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var org in orgs.EnumerateArray())
        {
            if (org.TryGetProperty("id", out var id))
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool UserExistsInContext(JsonElement data)
    {
        if (data.TryGetProperty("user", out var user)
            && user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("id", out var userId)
            && !string.IsNullOrWhiteSpace(userId.GetString()))
        {
            return true;
        }

        if (data.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (data.TryGetProperty("userExists", out var userExists) && userExists.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return false;
    }

    internal static bool UserExistsInContextForTest(JsonElement data) => UserExistsInContext(data);

    private static async Task<string?> TryLegacyLoginAsync(
        HttpClient http,
        SigNozUiCredentials credentials,
        CancellationToken cancellationToken)
    {
        var payload = new { email = credentials.AdminEmail, password = credentials.AdminPassword };

        try
        {
            using var response = await http.PostAsJsonAsync("/api/v1/login", payload, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return TryReadAccessToken(doc.RootElement);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadAccessToken(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("accessJwt", out var accessJwt))
            {
                return accessJwt.GetString();
            }

            if (data.TryGetProperty("accessToken", out var accessToken))
            {
                return accessToken.GetString();
            }
        }

        if (root.TryGetProperty("accessJwt", out var jwtRoot))
        {
            return jwtRoot.GetString();
        }

        if (root.TryGetProperty("accessToken", out var tokenRoot))
        {
            return tokenRoot.GetString();
        }

        return null;
    }

    private static async Task<Dictionary<string, List<ListedDashboard>>> ListDashboardsAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        var dashboards = new Dictionary<string, List<ListedDashboard>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var response = await http.GetAsync("/api/v2/dashboards", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return dashboards;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("data", out var data))
            {
                return dashboards;
            }

            foreach (var item in EnumerateListedDashboards(data))
            {
                var title = TryGetTitle(item);
                if (title is null)
                {
                    continue;
                }

                if (!dashboards.TryGetValue(title, out var list))
                {
                    list = [];
                    dashboards[title] = list;
                }

                list.Add(new ListedDashboard(title, TryGetDashboardId(item)));
            }
        }
        catch (HttpRequestException)
        {
            return dashboards;
        }
        catch (JsonException)
        {
            return dashboards;
        }

        return dashboards;
    }

    internal readonly record struct ListedDashboard(string Title, string? Id);

    internal static IEnumerable<JsonElement> EnumerateListedDashboardsForTest(JsonElement data) =>
        EnumerateListedDashboards(data);

    private static IEnumerable<JsonElement> EnumerateListedDashboards(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (data.TryGetProperty("dashboards", out var dashboards)
            && dashboards.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dashboards.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    internal static bool NeedsReplace(JsonElement existingRoot, JsonElement embeddedRoot)
    {
        var expected = GetLayoutTitles(embeddedRoot);
        if (expected.Count == 0)
        {
            return false;
        }

        var existing = GetLayoutTitles(existingRoot);
        return !expected.IsSubsetOf(existing);
    }

    internal static IReadOnlySet<string> GetLayoutTitles(JsonElement root)
    {
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (TryGetSpec(root, out var spec)
            && spec.TryGetProperty("layouts", out var layouts)
            && layouts.ValueKind == JsonValueKind.Array)
        {
            foreach (var layout in layouts.EnumerateArray())
            {
                if (layout.TryGetProperty("spec", out var layoutSpec)
                    && layoutSpec.TryGetProperty("display", out var display)
                    && display.TryGetProperty("title", out var title)
                    && title.ValueKind == JsonValueKind.String)
                {
                    var value = title.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        titles.Add(value);
                    }
                }
            }
        }

        return titles;
    }

    internal static string? TryGetDashboardId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            var value = id.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return TryGetDashboardId(data);
        }

        return null;
    }

    private static bool TryGetSpec(JsonElement root, out JsonElement spec)
    {
        if (root.TryGetProperty("spec", out spec) && spec.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return TryGetSpec(data, out spec);
        }

        spec = default;
        return false;
    }

    internal static string? TryGetTitle(JsonElement root)
    {
        if (TryGetSpec(root, out var spec)
            && spec.TryGetProperty("display", out var display)
            && display.TryGetProperty("name", out var displayName)
            && displayName.ValueKind == JsonValueKind.String)
        {
            var value = displayName.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var nested = TryGetTitle(data);
            if (nested is not null)
            {
                return nested;
            }
        }

        if (root.TryGetProperty("name", out var listedName)
            && listedName.ValueKind == JsonValueKind.String)
        {
            var value = listedName.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (root.TryGetProperty("title", out var title))
        {
            return title.GetString();
        }

        return null;
    }

    private static string ReadEmbedded(string logicalName)
    {
        var assembly = typeof(SigNozDashboardSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Uri? ResolveUiBaseAddress(SigNozResource resource)
    {
        if (!resource.TryGetEndpoints(out var endpoints))
        {
            return null;
        }

        var http = endpoints.FirstOrDefault(e => e.Name == SigNozResource.PrimaryEndpointName);
        var allocated = http?.AllocatedEndpoint;
        if (allocated is null)
        {
            return null;
        }

        return new Uri(allocated.UriString);
    }
}
