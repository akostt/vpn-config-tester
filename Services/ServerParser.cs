using System.Text.Json;
using System.Text.RegularExpressions;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Реализация парсера серверов из конфигурации VPN
/// Поддерживает протоколы: VLESS, Trojan, VMess, Shadowsocks, Hysteria
/// </summary>
public sealed class ServerParser(ILogger? logger = null) : IServerParser
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private const int MaxUrlLength = 2048;
    private const int MaxVmessBase64Length = 8192;
    private const int MaxShadowsocksBase64Length = 2048;

    private static readonly Regex VmessPattern = new(
        @"vmess://([A-Za-z0-9+/=_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ShadowsocksLegacyPattern = new(
        @"ss://([A-Za-z0-9+/=_-]+?)(?:#|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));
    
    private static readonly Regex ShadowsocksSIP002Pattern = new(
        @"ss://([^@]+)@(\[[^\]]+\]|[^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public IReadOnlyList<ServerInfo> ParseServers(IEnumerable<(string Line, string SourceUrl)> configLines)
    {
        if (configLines == null)
            throw new ArgumentNullException(nameof(configLines));

        var servers = new List<ServerInfo>();
        var parsedCount = 0;
        var errorCount = 0;

        foreach (var (line, sourceUrl) in configLines)
        {
            var trimmedLine = (line ?? string.Empty).Trim();
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
                server = server with { SourceConfigUrl = sourceUrl ?? string.Empty };
                servers.Add(server);
                parsedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                errorCount++;
            }
        }

        _logger.LogInfo($"Парсинг завершен: {parsedCount} серверов, {errorCount} ошибок");

        return servers;
    }

    private ServerInfo? ParseVlessUrl(string url) => ParseUserInfoUrl(url, "vless", requireUserInfo: true);

    private ServerInfo? ParseTrojanUrl(string url) => ParseUserInfoUrl(url, "trojan", requireUserInfo: true);

    private ServerInfo? ParseHysteriaUrl(string url) => ParseHysteriaUrlInternal(url);

    private ServerInfo? ParseHysteria2Url(string url) => ParseUserInfoUrl(url, "hysteria2", requireUserInfo: false);

    private ServerInfo? ParseHysteriaUrlInternal(string url)
    {
        try
        {
            var authority = GetAuthority(url, "hysteria");
            if (string.IsNullOrWhiteSpace(authority))
                return null;

            if (authority.Contains('@'))
                authority = authority[(authority.LastIndexOf('@') + 1)..];

            if (!TryParseHostPort(authority, out var host, out var port))
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
            _logger.LogInfo($"Парсинг: ошибка hysteria URL: {ex.Message}");
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
                    
                jsonData = Base64Helper.Decode(base64Data) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(jsonData))
                    return null;
            }
            catch (Exception)
            {
                _logger.LogInfo("Парсинг: ошибка base64 VMess");
                return null;
            }
            
            using var document = JsonDocument.Parse(jsonData);
            var root = document.RootElement;
            var host = GetJsonString(root, "add");
            var portStr = GetJsonString(root, "port");

            if (string.IsNullOrEmpty(host) || !TryParsePort(portStr, out var port))
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
            _logger.LogInfo($"Парсинг: ошибка vmess URL: {ex.Message}");
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
                var userInfo = Uri.UnescapeDataString(sip002Match.Groups[1].Value);
                var host = TrimIpv6Brackets(sip002Match.Groups[2].Value);
                
                if (!TryParsePort(sip002Match.Groups[3].Value, out var port))
                    return null;

                var decodedUserInfo = userInfo.Contains(':')
                    ? userInfo
                    : Base64Helper.Decode(CleanBase64String(userInfo));

                var colonIndex = decodedUserInfo?.IndexOf(':') ?? -1;
                if (!string.IsNullOrWhiteSpace(decodedUserInfo) &&
                    colonIndex > 0 &&
                    colonIndex < decodedUserInfo.Length - 1 &&
                    !decodedUserInfo.Contains('@'))
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
            
            var legacyMatch = ShadowsocksLegacyPattern.Match(url);
            if (!legacyMatch.Success || legacyMatch.Groups.Count < 2)
                return null;

            var base64DataLegacy = CleanBase64String(legacyMatch.Groups[1].Value);
            
            string decodedLegacy;
            try
            {
                if (base64DataLegacy.Length > MaxShadowsocksBase64Length)
                    return null;
                    
                decodedLegacy = Base64Helper.Decode(base64DataLegacy) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(decodedLegacy))
                    return null;
            }
            catch
            {
                _logger.LogInfo("Парсинг: ошибка base64 Shadowsocks");
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
                    
                    if (!TryParsePort(portPart, out var parsedPort))
                        return null;

                    port = parsedPort;
                }
                else
                {
                    var lastColonIndex = serverPart.LastIndexOf(':');
                    if (lastColonIndex == -1)
                        return null;
                    
                    host = serverPart.Substring(0, lastColonIndex);
                    if (!TryParsePort(serverPart.Substring(lastColonIndex + 1), out var parsedPort))
                        return null;

                    port = parsedPort;
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
            _logger.LogInfo($"Парсинг: ошибка shadowsocks URL: {ex.Message}");
            return null;
        }
    }

    private static string GetJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
    }

    private static string CleanBase64String(string base64Data)
    {
        var withoutFragment = base64Data.Split('#', 2)[0];
        var withoutQuery = withoutFragment.Split('?', 2)[0];
        var decoded = Uri.UnescapeDataString(withoutQuery);

        if (decoded.Length <= 512 && decoded.IndexOfAny(new[] { '\n', '\r', ' ', '\t' }) == -1)
            return decoded;
            
        return string.Concat(decoded.Where(c => c != '\n' && c != '\r' && c != ' ' && c != '\t'));
    }

    private ServerInfo? ParseUserInfoUrl(string url, string protocol, bool requireUserInfo)
    {
        try
        {
            var authority = GetAuthority(url, protocol);
            if (string.IsNullOrWhiteSpace(authority))
                return null;

            var atIndex = authority.LastIndexOf('@');
            if (requireUserInfo && atIndex <= 0)
                return null;

            var hostPort = atIndex >= 0 ? authority[(atIndex + 1)..] : authority;
            if (!TryParseHostPort(hostPort, out var host, out var port))
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
            _logger.LogInfo($"Парсинг: ошибка {protocol} URL: {ex.Message}");
            return null;
        }
    }

    private static string? GetAuthority(string url, string scheme)
    {
        var prefix = $"{scheme}://";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = url[prefix.Length..];
        var endIndex = remainder.IndexOfAny(new[] { '/', '?', '#' });
        return endIndex >= 0 ? remainder[..endIndex] : remainder;
    }

    private static bool TryParseHostPort(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.StartsWith('['))
        {
            var closeBracketIndex = value.IndexOf(']');
            if (closeBracketIndex <= 1 || closeBracketIndex + 2 > value.Length || value[closeBracketIndex + 1] != ':')
                return false;

            host = value[1..closeBracketIndex];
            return TryParsePort(value[(closeBracketIndex + 2)..], out port);
        }

        var lastColonIndex = value.LastIndexOf(':');
        if (lastColonIndex <= 0 || lastColonIndex >= value.Length - 1)
            return false;

        host = value[..lastColonIndex];
        return TryParsePort(value[(lastColonIndex + 1)..], out port);
    }

    private static bool TryParsePort(string? value, out int port)
    {
        port = 0;
        if (!int.TryParse(value, out var parsed) || parsed is <= 0 or > 65535)
            return false;

        port = parsed;
        return true;
    }

    private static string TrimIpv6Brackets(string host)
    {
        return host.Length > 1 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;
    }

}
