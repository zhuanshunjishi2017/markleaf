using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Updates;

internal sealed record GitHubReleaseInfo(
    Version Version,
    string VersionText,
    string BuildNumber,
    string InstallerName,
    Uri InstallerUri);

internal sealed class GitHubUpdateService
{
    private const string RepositoryApi = "https://api.github.com/repos/zhuanshunjishi2017/markleaf/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task<GitHubReleaseInfo?> FindUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(RepositoryApi, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = release.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!TryParseVersion(tag, out var version) || version <= currentVersion)
        {
            return null;
        }

        var assets = root.TryGetProperty("assets", out var assetsElement)
            ? assetsElement.EnumerateArray().ToArray()
            : [];
        var architecture = GetArchitectureLabel();
        var installer = assets.FirstOrDefault(asset =>
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            return name is not null
                && name.EndsWith($"-{architecture}.exe", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("with-runtime", StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var urlElement)
                && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out _);
        });
        if (installer.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"No Windows installer was found for {architecture}.");
        }

        var installerName = installer.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("The update installer has no file name.");
        var installerUrl = installer.GetProperty("browser_download_url").GetString()
            ?? throw new InvalidOperationException("The update installer has no download URL.");
        var installerUri = new Uri(installerUrl);
        var buildNumber = assets
            .Select(asset => asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null)
            .OfType<string>()
            .Select(name => Regex.Match(name, @"^MarkLeaf-build-(?<build>\d+)\.txt$", RegexOptions.IgnoreCase))
            .FirstOrDefault(match => match.Success)?.Groups["build"].Value ?? "?";

        return new GitHubReleaseInfo(version, version.ToString(3), buildNumber, installerName, installerUri);
    }

    public async Task<string> DownloadInstallerAsync(GitHubReleaseInfo release, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MarkLeaf", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, release.InstallerName);
        using var response = await Client.GetAsync(release.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        await input.CopyToAsync(output, cancellationToken);
        return path;
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimStart('v', 'V');
        var separator = normalized.IndexOf('-');
        if (separator >= 0) normalized = normalized[..separator];
        if (!Version.TryParse(normalized, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string GetArchitectureLabel() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => throw new PlatformNotSupportedException("Unsupported Windows process architecture."),
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarkLeaf", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
