using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Реализация сервиса для записи результатов конфигурации
/// </summary>
public sealed class ConfigWriter(ILogger? logger = null) : IConfigWriter
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

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
            .Select(s => FormatEndpoint(s.GetIpAddressOrHost(), s.Port))
            .ToList();
        
        await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
        
        _logger.LogInfo($"Сохранено {servers.Count} успешных серверов в {filePath}");
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

        var hostToIpMap = BuildHostToIpMap(successfulServers);

        var outputLines = successfulServers
            .Select(s => ReplaceHostnamesWithIp(s.OriginalUrl, hostToIpMap))
            .ToList();

        await File.WriteAllLinesAsync(outputFilePath, outputLines, Encoding.UTF8, cancellationToken);
        
        _logger.LogInfo($"Создан выходной конфиг с {outputLines.Count} уникальными конфигами в {outputFilePath}");
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
            result = Regex.Replace(result, pattern, _ => ipAddress, RegexOptions.IgnoreCase);
        }
        
        return result;
    }

    private static string FormatEndpoint(string host, int port)
    {
        var formattedHost = host.Contains(':') ? $"[{host}]" : host;
        return $"{formattedHost}:{port}";
    }
}
