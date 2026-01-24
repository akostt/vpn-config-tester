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
    
    // Compiled regex для эффективного извлечения JSON значений
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

    // Поддержка двух форматов Shadowsocks URL:
    // Legacy: ss://base64(method:password@host:port)#fragment
    // SIP002: ss://base64(method:password)@host:port#fragment
    private static readonly Regex ShadowsocksLegacyPattern = new(
        @"ss://([A-Za-z0-9+/=\s]+?)(?:#|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));
    
    private static readonly Regex ShadowsocksSIP002Pattern = new(
        @"ss://([A-Za-z0-9+/=\s]+?)@([^:]+):(\d+)",
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
            // Hysteria2 проверяется раньше Hysteria для точного совпадения протокола
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

    private ServerInfo? ParseHysteriaUrl(string url) => ParseHysteriaUrlInternal(url);

    private ServerInfo? ParseHysteria2Url(string url) => ParseUrl(url, Hysteria2Pattern, "hysteria2");

    /// <summary>
    /// Парсит Hysteria URL с поддержкой IPv6
    /// </summary>
    private ServerInfo? ParseHysteriaUrlInternal(string url)
    {
        try
        {
            var match = HysteriaPattern.Match(url);
            if (!match.Success || match.Groups.Count < 4)
                return null;

            // Group 1: IPv6 address (в скобках), Group 2: IPv4/hostname, Group 3: port
            var host = !string.IsNullOrEmpty(match.Groups[1].Value) 
                ? match.Groups[1].Value  // IPv6
                : match.Groups[2].Value; // IPv4 или hostname
                
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

            var base64Data = match.Groups[1].Value.Trim();
            
            // Валидация и декодирование base64
            string jsonData;
            try
            {
                // Проверка длины base64 данных
                if (base64Data.Length > MaxVmessBase64Length)
                    return null;
                
                // Очистка base64 от возможных пробелов и переносов строк
                base64Data = base64Data.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                    
                jsonData = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            }
            catch (FormatException)
            {
                logger?.LogWarning("⚠ Ошибка декодирования base64 в VMess URL");
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
    /// Поддерживает два формата:
    /// - Legacy: ss://base64(method:password@server:port)#fragment
    /// - SIP002: ss://base64(method:password)@server:port#fragment
    /// Поддерживает IPv4 и IPv6 адреса
    /// </summary>
    private ServerInfo? ParseShadowsocksUrl(string url)
    {
        try
        {
            // Сначала пробуем SIP002 формат (modern): ss://base64(method:password)@host:port
            var sip002Match = ShadowsocksSIP002Pattern.Match(url);
            if (sip002Match.Success && sip002Match.Groups.Count >= 4)
            {
                var base64Data = sip002Match.Groups[1].Value.Trim();
                var host = sip002Match.Groups[2].Value;
                
                if (!int.TryParse(sip002Match.Groups[3].Value, out var port))
                    return null;
                
                // Валидация base64 (декодируем для проверки, но не используем содержимое)
                try
                {
                    if (base64Data.Length > MaxShadowsocksBase64Length)
                        return null;
                    
                    // Очистка base64 от возможных пробелов и переносов строк
                    base64Data = base64Data.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                    
                    // Проверяем, что base64 валиден (декодируем method:password)
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
                    
                    // SIP002 формат должен содержать method:password без @
                    if (decoded.Contains(':') && !decoded.Contains('@'))
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
                    // Продолжаем пробовать legacy формат
                }
            }
            
            // Пробуем legacy формат: ss://base64(method:password@host:port)
            var legacyMatch = ShadowsocksLegacyPattern.Match(url);
            if (!legacyMatch.Success || legacyMatch.Groups.Count < 2)
                return null;

            var base64DataLegacy = legacyMatch.Groups[1].Value.Trim();
            
            // Валидация и декодирование base64
            string decodedLegacy;
            try
            {
                // Проверка длины base64 данных
                if (base64DataLegacy.Length > MaxShadowsocksBase64Length)
                    return null;
                
                // Очистка base64 от возможных пробелов и переносов строк
                base64DataLegacy = base64DataLegacy.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                    
                decodedLegacy = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64DataLegacy));
            }
            catch (FormatException)
            {
                logger?.LogWarning("⚠ Ошибка декодирования base64 в Shadowsocks URL");
                return null;
            }

            // Формат legacy: method:password@server:port
            // Ограничиваем split до 2 частей на случай если пароль содержит @
            if (decodedLegacy.Contains('@'))
            {
                var parts = decodedLegacy.Split('@', 2);
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
                    
                    // После ] должен быть :port
                    if (closeBracketIndex + 1 >= serverPart.Length || serverPart[closeBracketIndex + 1] != ':')
                        return null;
                        
                    var portPart = serverPart.Substring(closeBracketIndex + 2);
                    
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
    /// Использует compiled regex для эффективной обработки
    /// </summary>
    private static string ExtractJsonValue(string json, string key)
    {
        // Экранируем ключ для предотвращения regex injection
        var escapedKey = Regex.Escape(key);
        
        // Ищем все совпадения и фильтруем по нужному ключу
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

