using System.Text.RegularExpressions;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация парсера серверов из конфигурации VPN
/// Поддерживает протоколы: VLESS, Trojan, VMess, Shadowsocks, Hysteria
/// </summary>
public sealed class ServerParser(ILogger? logger = null) : IServerParser
{
    private const int MaxUrlLength = 2048;
    private const int MaxVmessBase64Length = 1024;
    private const int MaxShadowsocksBase64Length = 512;
    
    private static readonly Regex JsonValuePattern = new(
        @"""(?<key>[^""]+)""\s*:\s*""?(?<value>[^,""}\]]+)""?",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));
    
    private static readonly Regex VlessPattern = new(
        @"vless://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex TrojanPattern = new(
        @"trojan://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex VmessPattern = new(
        @"vmess://([A-Za-z0-9+/=]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ShadowsocksLegacyPattern = new(
        @"ss://([A-Za-z0-9+/=]+?)(?:#|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));
    
    private static readonly Regex ShadowsocksSIP002Pattern = new(
        @"ss://([A-Za-z0-9+/=]+?)@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HysteriaPattern = new(
        @"hysteria://(?:\[([^\]]+)\]|([^:]+)):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex Hysteria2Pattern = new(
        @"hysteria2://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public IReadOnlyList<ServerInfo> ParseServers(IEnumerable<string> configLines)
    {
        if (configLines == null)
            throw new ArgumentNullException(nameof(configLines));

        var servers = new List<ServerInfo>();
        var parsedCount = 0;
        var errorCount = 0;

        foreach (var line in configLines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.Length > MaxUrlLength)
                continue;

            ServerInfo? server = null;

            if (trimmedLine.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                server = ParseVlessUrl(trimmedLine);
            else if (trimmedLine.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                server = ParseTrojanUrl(trimmedLine);
            else if (trimmedLine.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                server = ParseVmessUrl(trimmedLine);
            else if (trimmedLine.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                server = ParseShadowsocksUrl(trimmedLine);
            else if (trimmedLine.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase))
                server = ParseHysteria2Url(trimmedLine);
            else if (trimmedLine.StartsWith("hysteria://", StringComparison.OrdinalIgnoreCase))
                server = ParseHysteriaUrl(trimmedLine);

            if (server != null)
            {
                servers.Add(server);
                parsedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                errorCount++;
            }
        }

        logger?.LogInfo($"Парсинг завершен: {parsedCount} серверов, {errorCount} ошибок");

        return servers;
    }

    private ServerInfo? ParseVlessUrl(string url) => ParseUrl(url, VlessPattern, "vless");

    private ServerInfo? ParseTrojanUrl(string url) => ParseUrl(url, TrojanPattern, "trojan");

    private ServerInfo? ParseHysteriaUrl(string url) => ParseHysteriaUrlInternal(url);

    private ServerInfo? ParseHysteria2Url(string url) => ParseUrl(url, Hysteria2Pattern, "hysteria2");

    private ServerInfo? ParseHysteriaUrlInternal(string url)
    {
        try
        {
            var match = HysteriaPattern.Match(url);
            if (!match.Success || match.Groups.Count < 4)
                return null;

            var host = !string.IsNullOrEmpty(match.Groups[1].Value) 
                ? match.Groups[1].Value
                : match.Groups[2].Value;
                
            if (!int.TryParse(match.Groups[3].Value, out var port))
                return null;

            return new ServerInfo
            {
                Host = host,
                Port = port,
                OriginalUrl = url,
                Protocol = "hysteria"
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка парсинга hysteria URL: {ex.Message}");
            return null;
        }
    }

    private ServerInfo? ParseVmessUrl(string url)
    {
        try
        {
            var match = VmessPattern.Match(url);
            if (!match.Success || match.Groups.Count < 2)
                return null;

            var base64Data = CleanBase64String(match.Groups[1].Value);
            
            string jsonData;
            try
            {
                if (base64Data.Length > MaxVmessBase64Length)
                    return null;
                    
                jsonData = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            }
            catch (FormatException)
            {
                logger?.LogWarning("⚠ Ошибка декодирования base64 в VMess URL");
                return null;
            }
            
            var host = ExtractJsonValue(jsonData, "add");
            var portStr = ExtractJsonValue(jsonData, "port");

            if (string.IsNullOrEmpty(host) || !int.TryParse(portStr, out var port))
                return null;

            return new ServerInfo
            {
                Host = host,
                Port = port,
                OriginalUrl = url,
                Protocol = "vmess"
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка парсинга vmess URL: {ex.Message}");
            return null;
        }
    }

    private ServerInfo? ParseShadowsocksUrl(string url)
    {
        try
        {
            var sip002Match = ShadowsocksSIP002Pattern.Match(url);
            if (sip002Match.Success && sip002Match.Groups.Count >= 4)
            {
                var base64Data = CleanBase64String(sip002Match.Groups[1].Value);
                var host = sip002Match.Groups[2].Value;
                
                if (!int.TryParse(sip002Match.Groups[3].Value, out var port))
                    return null;
                
                try
                {
                    if (base64Data.Length > MaxShadowsocksBase64Length)
                        return null;
                    
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
                    var colonIndex = decoded.IndexOf(':');
                    var atIndex = decoded.IndexOf('@');
                    
                    if (colonIndex > 0 && colonIndex < decoded.Length - 1 && atIndex == -1)
                    {
                        return new ServerInfo
                        {
                            Host = host,
                            Port = port,
                            OriginalUrl = url,
                            Protocol = "shadowsocks"
                        };
                    }
                }
                catch (FormatException)
                {
                }
            }
            
            var legacyMatch = ShadowsocksLegacyPattern.Match(url);
            if (!legacyMatch.Success || legacyMatch.Groups.Count < 2)
                return null;

            var base64DataLegacy = CleanBase64String(legacyMatch.Groups[1].Value);
            
            string decodedLegacy;
            try
            {
                if (base64DataLegacy.Length > MaxShadowsocksBase64Length)
                    return null;
                    
                decodedLegacy = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64DataLegacy));
            }
            catch (FormatException)
            {
                logger?.LogWarning("⚠ Ошибка декодирования base64 в Shadowsocks URL");
                return null;
            }

            if (decodedLegacy.Contains('@'))
            {
                var parts = decodedLegacy.Split('@', 2);
                if (parts.Length != 2)
                    return null;

                var serverPart = parts[1];
                string host;
                int port;
                
                if (serverPart.StartsWith('['))
                {
                    var closeBracketIndex = serverPart.IndexOf(']');
                    if (closeBracketIndex == -1)
                        return null;
                    
                    host = serverPart.Substring(1, closeBracketIndex - 1);
                    
                    if (closeBracketIndex + 1 >= serverPart.Length || serverPart[closeBracketIndex + 1] != ':')
                        return null;
                        
                    var portPart = serverPart.Substring(closeBracketIndex + 2);
                    
                    if (!int.TryParse(portPart, out port))
                        return null;
                }
                else
                {
                    var lastColonIndex = serverPart.LastIndexOf(':');
                    if (lastColonIndex == -1)
                        return null;
                    
                    host = serverPart.Substring(0, lastColonIndex);
                    if (!int.TryParse(serverPart.Substring(lastColonIndex + 1), out port))
                        return null;
                }

                return new ServerInfo
                {
                    Host = host,
                    Port = port,
                    OriginalUrl = url,
                    Protocol = "shadowsocks"
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка парсинга shadowsocks URL: {ex.Message}");
            return null;
        }
    }

    private static string ExtractJsonValue(string json, string key)
    {
        var escapedKey = Regex.Escape(key);
        var matches = JsonValuePattern.Matches(json);
        foreach (Match match in matches)
        {
            if (match.Groups["key"].Value.Equals(escapedKey, StringComparison.Ordinal))
            {
                return match.Groups["value"].Value.Trim();
            }
        }
        
        return string.Empty;
    }

    private static string CleanBase64String(string base64Data)
    {
        if (base64Data.Length <= 512 && base64Data.IndexOfAny(new[] { '\n', '\r', ' ', '\t' }) == -1)
            return base64Data;
            
        return string.Concat(base64Data.Where(c => c != '\n' && c != '\r' && c != ' ' && c != '\t'));
    }

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

