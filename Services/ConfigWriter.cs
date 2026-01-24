using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для записи результатов конфигурации
/// </summary>
public sealed class ConfigWriter(ILogger? logger = null) : IConfigWriter
{
    /// <summary>
    /// Сохраняет список успешных серверов в файл
    /// </summary>
    public async Task SaveSuccessfulServersAsync(
        IReadOnlyList<ServerInfo> servers,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (servers == null)
            throw new ArgumentNullException(nameof(servers));
        
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        var lines = servers
            .Select(s => $"{s.GetIpAddressOrHost()}:{s.Port}")
            .ToList();
        
        await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
        
        logger?.LogInfo($"Сохранено {servers.Count} успешных серверов в {filePath}");
    }

    public async Task CreateOutputConfigAsync(
        IReadOnlyList<ServerInfo> successfulServers,
        string outputFilePath,
        IEnumerable<string> originalLines,
        CancellationToken cancellationToken = default)
    {
        if (successfulServers == null)
            throw new ArgumentNullException(nameof(successfulServers));
        
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path cannot be null or empty", nameof(outputFilePath));
        
        if (originalLines == null)
            throw new ArgumentNullException(nameof(originalLines));

        var hostToIpMap = BuildHostToIpMap(successfulServers);

        var outputLines = originalLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => ContainsSuccessfulServer(line, successfulServers))
            .Select(line => ReplaceHostnamesWithIp(line, hostToIpMap))
            .ToList();

        var uniqueLines = RemoveDuplicateUrls(outputLines);

        await File.WriteAllLinesAsync(outputFilePath, uniqueLines, Encoding.UTF8, cancellationToken);
        
        var duplicatesRemoved = outputLines.Count - uniqueLines.Count;
        if (duplicatesRemoved > 0)
        {
            logger?.LogInfo($"Создан выходной конфиг с {uniqueLines.Count} строками в {outputFilePath} (удалено дубликатов: {duplicatesRemoved})");
        }
        else
        {
            logger?.LogInfo($"Создан выходной конфиг с {uniqueLines.Count} строками в {outputFilePath}");
        }
    }

    private static Dictionary<string, string> BuildHostToIpMap(IReadOnlyList<ServerInfo> servers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var server in servers)
        {
            var ipAddress = server.GetIpAddressOrHost();
            if (!string.Equals(server.Host, ipAddress, StringComparison.OrdinalIgnoreCase))
            {
                map[server.Host] = ipAddress;
            }
        }

        return map;
    }

    private static bool ContainsSuccessfulServer(string line, IReadOnlyList<ServerInfo> successfulServers)
    {
        return successfulServers.Any(server =>
            line.Contains(server.Host, StringComparison.OrdinalIgnoreCase) &&
            line.Contains($":{server.Port}", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReplaceHostnamesWithIp(string line, Dictionary<string, string> hostToIpMap)
    {
        if (hostToIpMap.Count == 0)
            return line;

        var result = line;
        var sortedHostnames = hostToIpMap.Keys.OrderByDescending(h => h.Length).ToList();
        
        foreach (var hostname in sortedHostnames)
        {
            var ipAddress = hostToIpMap[hostname];
            var escapedHostname = Regex.Escape(hostname);
            var pattern = $@"(?<=@|://)({escapedHostname})(?=[:?]|$)";
            result = Regex.Replace(result, pattern, ipAddress, RegexOptions.IgnoreCase);
        }
        
        return result;
    }

    private static List<string> RemoveDuplicateUrls(List<string> urls)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();

        foreach (var url in urls)
        {
            var normalizedUrl = NormalizeUrl(url);
            if (seen.Add(normalizedUrl))
            {
                result.Add(url);
            }
        }

        return result;
    }

    private static string NormalizeUrl(string url)
    {
        var hashIndex = url.IndexOf('#');
        return hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
    }
}

