using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Сервис для скачивания и поиска sing-box
/// </summary>
public sealed class SingBoxManager(ApplicationConfiguration config, ILogger? logger = null) : ISingBoxManager
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<string?> EnsureSingBoxAsync(CancellationToken cancellationToken = default)
    {
        var explicitPath = _config.SingBoxExecutablePath;
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var localPath = GetLocalBinaryPath();
        if (File.Exists(localPath))
            return localPath;

        var fromPath = TryFindInPath("sing-box");
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath;

        try
        {
            Directory.CreateDirectory(_config.SingBoxToolsDirectory);
            logger?.LogInfo("sing-box не найден. Скачиваю бинарник...");
            var downloadedPath = await DownloadLatestSingBoxAsync(localPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath))
            {
                logger?.LogInfo($"sing-box скачан: {downloadedPath}");
                return downloadedPath;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Не удалось скачать sing-box: {ex.Message}");
        }

        return null;
    }

    private string GetLocalBinaryPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "sing-box.exe" : "sing-box";
        return Path.Combine(_config.SingBoxToolsDirectory, fileName);
    }

    private static string? TryFindInPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in paths)
        {
            var candidate = Path.Combine(path, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async Task<string?> DownloadLatestSingBoxAsync(string targetPath, CancellationToken cancellationToken)
    {
        var (tag, assetName) = await ResolveLatestAssetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(assetName))
            return null;

        var url = $"https://github.com/SagerNet/sing-box/releases/download/{tag}/{assetName}";
        var tempDir = Path.Combine(Path.GetTempPath(), "singbox_download");
        Directory.CreateDirectory(tempDir);

        var archivePath = Path.Combine(tempDir, assetName);
        using (var httpClient = CreateHttpClient())
        using (var response = await httpClient.GetAsync(url, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(archivePath);
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        var extractDir = Path.Combine(tempDir, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            var tarPath = Path.Combine(tempDir, "sing-box.tar");
            await using (var inStream = File.OpenRead(archivePath))
            await using (var gzip = new GZipStream(inStream, CompressionMode.Decompress))
            await using (var outStream = File.Create(tarPath))
            {
                await gzip.CopyToAsync(outStream, cancellationToken);
            }
            TarFile.ExtractToDirectory(tarPath, extractDir, true);
        }
        else
        {
            return null;
        }

        var binaryName = OperatingSystem.IsWindows() ? "sing-box.exe" : "sing-box";
        var extractedBinary = Directory.GetFiles(extractDir, binaryName, SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extractedBinary))
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
        File.Copy(extractedBinary, targetPath, true);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(targetPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                // ignore chmod errors
            }
        }

        return targetPath;
    }

    private async Task<(string Tag, string AssetName)> ResolveLatestAssetAsync(CancellationToken cancellationToken)
    {
        var os = GetOsName();
        var arch = GetArchName();
        if (string.IsNullOrWhiteSpace(os) || string.IsNullOrWhiteSpace(arch))
            return (string.Empty, string.Empty);

        using var httpClient = CreateHttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/SagerNet/sing-box/releases/latest");
        request.Headers.UserAgent.ParseAdd("vpn-config-tester");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var assetName = OperatingSystem.IsWindows()
            ? $"sing-box-{tag.TrimStart('v')}-{os}-{arch}.zip"
            : $"sing-box-{tag.TrimStart('v')}-{os}-{arch}.tar.gz";

        // GitHub assets use tag with v prefix in URL, but filename uses version without v
        return (tag, assetName);
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "darwin";
        if (OperatingSystem.IsLinux()) return "linux";
        return string.Empty;
    }

    private static string GetArchName()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "amd64",
            Architecture.X86 => "386",
            _ => string.Empty
        };
    }
}
