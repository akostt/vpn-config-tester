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
    // Защита от ReDoS атак: ограничение длины строк и использование безопасных паттернов
    private const int MaxUrlLength = 2048;
    private const int MaxVmessBase64Length = 1024;
    private const int MaxShadowsocksBase64Length = 512;
    
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

    private static readonly Regex ShadowsocksPattern = new(
        @"ss://([A-Za-z0-9+/=]+)(?:#.*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HysteriaPattern = new(
        @"hysteria://([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex Hysteria2Pattern = new(
        @"hysteria2://[^@]+@([^:]+):(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Парсит строки конфигурации и извлекает информацию о серверах
    /// Поддерживает: VLESS, Trojan, VMess, Shadowsocks, Hysteria, Hysteria2
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
            // Важно: hysteria2:// должен проверяться раньше hysteria://,
            // иначе hysteria:// совпадет с префиксом "hysteria" в "hysteria2://"
            else if (trimmedLine.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase))
                server = ParseHysteria2Url(trimmedLine);
            else if (trimmedLine.StartsWith("hysteria://", StringComparison.OrdinalIgnoreCase))
                server = ParseHysteriaUrl(trimmedLine);

            if (server != null)
            {
                if (servers.Add(server))
                    parsedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                errorCount++;
            }
        }

        logger?.LogInfo($"Парсинг завершен: {parsedCount} уникальных серверов, {errorCount} ошибок");

        return servers.ToList();
    }

    private ServerInfo? ParseVlessUrl(string url) => ParseUrl(url, VlessPattern, "vless");

    private ServerInfo? ParseTrojanUrl(string url) => ParseUrl(url, TrojanPattern, "trojan");

    private ServerInfo? ParseHysteriaUrl(string url) => ParseUrl(url, HysteriaPattern, "hysteria");

    private ServerInfo? ParseHysteria2Url(string url) => ParseUrl(url, Hysteria2Pattern, "hysteria2");

    /// <summary>
    /// Парсит VMess URL (base64 encoded JSON)
    /// </summary>
    private ServerInfo? ParseVmessUrl(string url)
    {
        try
        {
            var match = VmessPattern.Match(url);
            if (!match.Success || match.Groups.Count < 2)
                return null;

            var base64Data = match.Groups[1].Value;
            
            // Валидация и декодирование base64
            string jsonData;
            try
            {
                // Проверка длины base64 данных
                if (base64Data.Length > MaxVmessBase64Length)
                    return null;
                    
                jsonData = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            }
            catch (FormatException)
            {
                logger?.LogWarning("Ошибка декодирования base64 в VMess URL");
                return null;
            }
            
            // Простой парсинг JSON (без дополнительных библиотек)
            // VMess использует поля 'add' для адреса и 'port' для порта
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

    /// <summary>
    /// Парсит Shadowsocks URL (base64 encoded)
    /// Формат: ss://base64(method:password@server:port)
    /// Поддерживает IPv4 и IPv6 адреса
    /// </summary>
    private ServerInfo? ParseShadowsocksUrl(string url)
    {
        try
        {
            var match = ShadowsocksPattern.Match(url);
            if (!match.Success || match.Groups.Count < 2)
                return null;

            var base64Data = match.Groups[1].Value;
            
            // Валидация и декодирование base64
            string decoded;
            try
            {
                // Проверка длины base64 данных
                if (base64Data.Length > MaxShadowsocksBase64Length)
                    return null;
                    
                decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            }
            catch (FormatException)
            {
                logger?.LogWarning("Ошибка декодирования base64 в Shadowsocks URL");
                return null;
            }

            // Формат: method:password@server:port
            if (decoded.Contains('@'))
            {
                var parts = decoded.Split('@');
                if (parts.Length != 2)
                    return null;

                var serverPart = parts[1];
                
                // Обработка IPv6 адресов в квадратных скобках [::1]:port
                string host;
                int port;
                
                if (serverPart.StartsWith('['))
                {
                    var closeBracketIndex = serverPart.IndexOf(']');
                    if (closeBracketIndex == -1)
                        return null;
                    
                    host = serverPart.Substring(1, closeBracketIndex - 1);
                    var portPart = serverPart.Substring(closeBracketIndex + 1).TrimStart(':');
                    
                    if (!int.TryParse(portPart, out port))
                        return null;
                }
                else
                {
                    // IPv4 или hostname
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

    /// <summary>
    /// Извлекает значение из простого JSON по ключу
    /// Безопасный метод с экранированием ключа
    /// </summary>
    private static string ExtractJsonValue(string json, string key)
    {
        // Экранируем ключ для предотвращения regex injection
        var escapedKey = Regex.Escape(key);
        // Паттерн для поиска значения в JSON: "key": "value" или "key": value
        var pattern = $"\"{escapedKey}\"\\s*:\\s*\"?([^,\"\\u007D]+)\"?";
        var match = Regex.Match(json, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
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

