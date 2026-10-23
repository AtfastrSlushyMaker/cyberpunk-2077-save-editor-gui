using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>A newer release on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? ZipUrl, string Notes);

/// <summary>
/// Checks the project's GitHub releases for a newer version and installs it: the release zip is downloaded and
/// extracted, then a small script waits for the editor to close, copies the new files over and starts it again.
/// </summary>
public static class UpdateService
{
    public const string Repository = "AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui";
    public static string RepositoryUrl => $"https://github.com/{Repository}";
    public static string ReleasesUrl => $"{RepositoryUrl}/releases";

    public static Version CurrentVersion { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private static readonly HttpClient s_http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CP2077SaveEditor/" + CurrentVersion.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release if it is newer than this build, otherwise null. Throws when GitHub cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var response = await s_http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // no release published yet
        }
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || Normalize(version) <= Normalize(CurrentVersion))
        {
            return null;
        }

        var zip = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString()))
                .FirstOrDefault(a => a.Name.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Url
            : null;
        return new UpdateInfo(version, tag, root.GetProperty("html_url").GetString() ?? ReleasesUrl, zip,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    /// <summary>
    /// Downloads and unpacks the update, then starts the script that replaces this build once the editor exits.
    /// The caller shuts the editor down after this returns.
    /// </summary>
    public static async Task PrepareInstallAsync(UpdateInfo update, IProgress<string>? progress = null)
    {
        if (update.ZipUrl == null)
        {
            throw new InvalidOperationException("This release has no download for Windows.");
        }
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown program location.");
        var appDir = Path.GetDirectoryName(exe)!;
        var work = Path.Combine(Path.GetTempPath(), "CP2077SaveEditor-update");
        if (Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }
        Directory.CreateDirectory(work);

        progress?.Report($"Downloading {update.Tag}…");
        var zipPath = Path.Combine(work, "update.zip");
        await using (var source = await s_http.GetStreamAsync(update.ZipUrl))
        await using (var target = File.Create(zipPath))
        {
            await source.CopyToAsync(target);
        }

        progress?.Report("Unpacking…");
        var extracted = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zipPath, extracted);
        // the zip may hold the files directly or inside one folder
        var payload = Directory.GetFiles(extracted, "*.exe").Length > 0 ? extracted
            : Directory.GetDirectories(extracted).FirstOrDefault(d => Directory.GetFiles(d, "*.exe").Length > 0)
              ?? throw new InvalidDataException("The download does not contain the editor.");
        var newExe = Directory.GetFiles(payload, "*.exe").First();

        var script = Path.Combine(work, "install.cmd");
        File.WriteAllText(script, $"""
            @echo off
            rem Waits for the editor to close, copies the new version over it and starts it again.
            :wait
            tasklist /FI "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
            robocopy "{payload}" "{appDir}" /E /R:5 /W:1 >nul
            start "" "{Path.Combine(appDir, Path.GetFileName(newExe))}"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = work });
    }

    /// <summary>"1.2" and "1.2.0.0" compare equal.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Opens a web page in the default browser.</summary>
    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
