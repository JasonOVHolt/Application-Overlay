using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OverlayApp;

internal sealed record UpdateInfo(Version Version, string Tag, string Name, string PageUrl);

internal enum UpdateCheckStatus { UpdateAvailable, UpToDate, NoReleases, Failed }

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null, string? Error = null);

/// <summary>
/// Compares this build's version with the latest GitHub release of <see cref="Repository"/>.
/// Both come from the project file (&lt;Version&gt; and &lt;UpdateRepository&gt;).
/// </summary>
internal static class UpdateChecker
{
    // Static initializers run top to bottom: these must come before Http, whose User-Agent uses the version.
    public static Version CurrentVersion { get; } =
        Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    public static string? Repository { get; } = Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value;

    private static readonly HttpClient Http = CreateClient();

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    private static HttpClient CreateClient()
    {
        // Follows redirects, so a renamed or transferred repository keeps working.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"OverlayApp/{Format(CurrentVersion)}"); // required by GitHub's API
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Repository))
            return new(UpdateCheckStatus.Failed, Error: "No update repository is configured.");

        try
        {
            // "latest" skips drafts and pre-releases.
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckStatus.NoReleases);
            if (!response.IsSuccessStatusCode)
                return new(UpdateCheckStatus.Failed, Error: $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string name = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : tag;
            string page = root.TryGetProperty("html_url", out var u) && u.GetString() is { } url ? url : ReleasesPage;

            if (!TryParseVersion(tag, out var latest))
                return new(UpdateCheckStatus.Failed, Error: $"Couldn't read a version number from the release tag \"{tag}\".");
            return latest > CurrentVersion
                ? new(UpdateCheckStatus.UpdateAvailable, new UpdateInfo(latest, tag, name, page))
                : new(UpdateCheckStatus.UpToDate);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(UpdateCheckStatus.Failed, Error: "GitHub didn't respond in time.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(UpdateCheckStatus.Failed, Error: e.Message);
        }
    }

    /// <summary>Reads the version from a tag such as "V2.0", "v1.2.3", "2.1" or "release-3.0".</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        var match = Regex.Match(tag, @"\d+(\.\d+){0,3}");
        if (match.Success && Version.TryParse(match.Value.Contains('.') ? match.Value : match.Value + ".0", out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>Three-part version, so "2.0" and "2.0.0" compare equal (System.Version treats them as different).</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static string Format(Version v) => v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";

    /// <summary>Opens a GitHub page in the browser (only github.com links, since the URL comes from the network).</summary>
    public static void OpenInBrowser(string url)
    {
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
