using System.Text.RegularExpressions;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация парсера серверов из конфигурации VPN
/// </summary>
public sealed class ServerParser : IServerParser
{
    private static readonly Regex VlessPattern = new(
        @"vless://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrojanPattern = new(
        @"trojan://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger? _logger;

    public ServerParser(ILogger? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<ServerInfo> ParseServers(IEnumerable<string> configLines)
    {
        if (configLines == null)
            throw new ArgumentNullException(nameof(configLines));

        var servers = new HashSet<ServerInfo>();
        int parsedCount = 0;
        int errorCount = 0;

        foreach (var line in configLines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            ServerInfo? server = null;

            if (trimmedLine.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                server = ParseVlessUrl(trimmedLine);
            }
            else if (trimmedLine.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
            {
                server = ParseTrojanUrl(trimmedLine);
            }

            if (server != null)
            {
                if (servers.Add(server))
                {
                    parsedCount++;
                }
            }
            else
            {
                errorCount++;
            }
        }

        _logger?.LogInfo($"Парсинг завершен: {parsedCount} уникальных серверов, {errorCount} ошибок");

        return servers.ToList();
    }

    private ServerInfo? ParseVlessUrl(string url)
    {
        try
        {
            var match = VlessPattern.Match(url);
            if (match.Success && match.Groups.Count >= 3)
            {
                var host = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var port))
                {
                    return new ServerInfo
                    {
                        Host = host,
                        Port = port,
                        OriginalUrl = url,
                        Protocol = "vless"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Ошибка парсинга vless URL: {ex.Message}");
        }

        return null;
    }

    private ServerInfo? ParseTrojanUrl(string url)
    {
        try
        {
            var match = TrojanPattern.Match(url);
            if (match.Success && match.Groups.Count >= 3)
            {
                var host = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var port))
                {
                    return new ServerInfo
                    {
                        Host = host,
                        Port = port,
                        OriginalUrl = url,
                        Protocol = "trojan"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Ошибка парсинга trojan URL: {ex.Message}");
        }

        return null;
    }
}

