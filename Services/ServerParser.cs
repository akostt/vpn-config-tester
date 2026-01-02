using System.Text.RegularExpressions;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация парсера серверов из конфигурации VPN
/// </summary>
public sealed class ServerParser(ILogger? logger = null) : IServerParser
{
    private static readonly Regex VlessPattern = new(
        @"vless://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrojanPattern = new(
        @"trojan://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Парсит строки конфигурации и извлекает информацию о серверах
    /// </summary>
    public IReadOnlyList<ServerInfo> ParseServers(IEnumerable<string> configLines)
    {
        if (configLines == null)
            throw new ArgumentNullException(nameof(configLines));

        var servers = new HashSet<ServerInfo>();
        var parsedCount = 0;
        var errorCount = 0;

        foreach (var line in configLines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            ServerInfo? server = trimmedLine.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)
                ? ParseVlessUrl(trimmedLine)
                : trimmedLine.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)
                    ? ParseTrojanUrl(trimmedLine)
                    : null;

            if (server != null)
            {
                if (servers.Add(server))
                    parsedCount++;
            }
            else
            {
                errorCount++;
            }
        }

        logger?.LogInfo($"Парсинг завершен: {parsedCount} уникальных серверов, {errorCount} ошибок");

        return servers.ToList();
    }

    private ServerInfo? ParseVlessUrl(string url) => ParseUrl(url, VlessPattern, "vless");

    private ServerInfo? ParseTrojanUrl(string url) => ParseUrl(url, TrojanPattern, "trojan");

    private ServerInfo? ParseUrl(string url, Regex pattern, string protocol)
    {
        try
        {
            var match = pattern.Match(url);
            if (!match.Success || match.Groups.Count < 3)
                return null;

            var host = match.Groups[1].Value;
            if (!int.TryParse(match.Groups[2].Value, out var port))
                return null;

            return new ServerInfo
            {
                Host = host,
                Port = port,
                OriginalUrl = url,
                Protocol = protocol
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка парсинга {protocol} URL: {ex.Message}");
            return null;
        }
    }
}

