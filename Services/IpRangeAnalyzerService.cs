using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Реализация сервиса для анализа IP-диапазонов
/// </summary>
public sealed class IpRangeAnalyzerService(ApplicationConfiguration config, ILogger? logger = null) : IIpRangeAnalyzer
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Dictionary<string, string> _providerCache = new(StringComparer.Ordinal);

    public IReadOnlyList<IpRange> AnalyzeIpRanges(string serversFile)
    {
        if (string.IsNullOrWhiteSpace(serversFile))
            throw new ArgumentException("Servers file path cannot be null or empty", nameof(serversFile));

        if (!File.Exists(serversFile))
        {
            _logger.LogWarning($"Файл {serversFile} не найден");
            return Array.Empty<IpRange>();
        }

        var ipAddresses = ExtractIpAddresses(serversFile);
        
        if (ipAddresses.Count == 0)
        {
            _logger.LogWarning("Не найдено IP-адресов для анализа");
            return Array.Empty<IpRange>();
        }

        return GroupIntoRanges(ipAddresses);
    }

    public void PrintAnalysis(IReadOnlyList<IpRange> ranges)
    {
        if (ranges == null || ranges.Count == 0)
        {
            _logger.LogResult("Нет данных для анализа");
            return;
        }

        _logger.LogResult($"Всего найдено уникальных подсетей: {ranges.Count}");

        PrintSubnet24Table(ranges);
        PrintSubnet16Table(ranges);
        PrintRecommendedRanges(ranges);
    }

    public void SaveRangesToFile(IReadOnlyList<IpRange> ranges, string outputFile)
    {
        if (ranges == null)
            throw new ArgumentNullException(nameof(ranges));
        
        if (string.IsNullOrWhiteSpace(outputFile))
            throw new ArgumentException("Output file path cannot be null or empty", nameof(outputFile));

        var largeSubnet16 = ranges
            .Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16)
            .Select(r => ExtractSubnet16Prefix(r.Network))
            .ToHashSet();

        var lines = new List<string>
        {
            "# Рекомендуемые IP-диапазоны для фильтрации успешных серверов",
            "# Формат: CIDR (количество IP)",
            "",
            "# Подсети /24 с 3+ IP (исключая те, что входят в крупные /16):",
            ""
        };

        var recommended24 = ranges
            .Where(r => r.Cidr.EndsWith("/24") && r.Count >= _config.MinIpCountForSubnet24)
            .Where(r =>
            {
                var subnet16Prefix = ExtractSubnet16Prefix(r.Network);
                return !largeSubnet16.Contains(subnet16Prefix);
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        foreach (var range in recommended24)
        {
            lines.Add($"{range.Cidr} # {range.Count} IP");
        }

        lines.Add("");
        lines.Add("# Крупные подсети /16 (5+ IP):");
        lines.Add("");

        var recommended16 = ranges
            .Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16)
            .OrderByDescending(r => r.Count)
            .ToList();

        foreach (var range in recommended16)
        {
            lines.Add($"{range.Cidr} # {range.Count} IP");
        }

        File.WriteAllLines(outputFile, lines);
    }

    private List<IPAddress> ExtractIpAddresses(string serversFile)
    {
        var ipAddresses = new List<IPAddress>();
        var lines = File.ReadAllLines(serversFile);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            // For plain IP / CIDR / ip:port lines (not VPN URIs):
            // strip inline comment and CIDR prefix before extracting host
            if (!trimmed.Contains("://"))
            {
                var commentIdx = trimmed.IndexOf('#');
                if (commentIdx >= 0) trimmed = trimmed[..commentIdx].Trim();
                var slashIdx = trimmed.IndexOf('/');
                if (slashIdx > 0) trimmed = trimmed[..slashIdx].Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
            }

            var hostString = ExtractHost(trimmed);
            if (!string.IsNullOrWhiteSpace(hostString) &&
                IPAddress.TryParse(hostString, out var ipAddress))
            {
                ipAddresses.Add(ipAddress);
            }
        }

        return ipAddresses;
    }

    private static string ExtractHost(string endpoint)
    {
        // Skip comments
        if (endpoint.StartsWith('#'))
            return string.Empty;

        // VPN URI: vless://uuid@host:port?params  or  trojan://pass@host:port
        var schemeEnd = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            var afterScheme = endpoint[(schemeEnd + 3)..];
            // strip userinfo (everything before last '@' before the host)
            var atIndex = afterScheme.LastIndexOf('@');
            var hostPart = atIndex >= 0 ? afterScheme[(atIndex + 1)..] : afterScheme;
            // strip query/fragment/path
            var qIndex = hostPart.IndexOfAny(['?', '#', '/']);
            if (qIndex >= 0) hostPart = hostPart[..qIndex];
            // IPv6 literal [::1]:port
            if (hostPart.StartsWith('['))
            {
                var close = hostPart.IndexOf(']');
                return close > 1 ? hostPart[1..close] : string.Empty;
            }
            // host:port → take host
            var colonIdx = hostPart.LastIndexOf(':');
            return colonIdx > 0 ? hostPart[..colonIdx].Trim() : hostPart.Trim();
        }

        // Plain ip:port or [ipv6]:port
        if (endpoint.StartsWith('['))
        {
            var closingBracketIndex = endpoint.IndexOf(']');
            return closingBracketIndex > 1
                ? endpoint[1..closingBracketIndex]
                : string.Empty;
        }

        var lastColonIndex = endpoint.LastIndexOf(':');
        return lastColonIndex > 0 ? endpoint[..lastColonIndex].Trim() : endpoint.Trim();
    }

    private List<IpRange> GroupIntoRanges(List<IPAddress> ipAddresses)
    {
        if (ipAddresses.Count == 0)
            return new List<IpRange>();

        var ranges = new List<IpRange>();

        var subnet24Groups = ipAddresses
            .GroupBy(GetSubnet24)
            .Select(g => new
            {
                Subnet = g.Key,
                IPs = g.OrderBy(ConvertIpToLong).ToList(),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var group in subnet24Groups)
        {
            ranges.Add(new IpRange
            {
                Network = group.Subnet,
                MinIp = group.IPs.First(),
                MaxIp = group.IPs.Last(),
                Count = group.Count,
                Cidr = $"{group.Subnet}/24"
            });
        }

        var subnet16Groups = ipAddresses
            .GroupBy(GetSubnet16)
            .Where(g => g.Count() >= _config.MinIpCountForSubnet16)
            .Select(g => new
            {
                Subnet = g.Key,
                Count = g.Count(),
                IPs = g.OrderBy(ConvertIpToLong).ToList()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var largeRange in subnet16Groups)
        {
            var existingRange = ranges.FirstOrDefault(r =>
                r.Network.StartsWith(largeRange.Subnet.Substring(0, largeRange.Subnet.LastIndexOf('.'))));

            if (existingRange == null || largeRange.Count >= 10)
            {
                ranges.Add(new IpRange
                {
                    Network = largeRange.Subnet,
                    MinIp = largeRange.IPs.First(),
                    MaxIp = largeRange.IPs.Last(),
                    Count = largeRange.Count,
                    Cidr = $"{largeRange.Subnet}/16"
                });
            }
        }

        return ranges.OrderByDescending(r => r.Count).ToList();
    }

    private static string GetSubnet24(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length < 4)
            throw new ArgumentException("IPv4 address expected", nameof(ip));
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0";
    }

    private static string GetSubnet16(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length < 4)
            throw new ArgumentException("IPv4 address expected", nameof(ip));
        return $"{bytes[0]}.{bytes[1]}.0.0";
    }

    private static long ConvertIpToLong(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4) // IPv4
        {
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }
        // Для IPv6 используем первые 8 байт
        long result = 0;
        for (var i = 0; i < Math.Min(8, bytes.Length); i++)
        {
            result = (result << 8) | bytes[i];
        }
        return result;
    }

    private const int Sep = 108;
    private static readonly string SepDouble = new string('═', Sep);
    private static readonly string SepSingle = new string('─', Sep);
    private static readonly string Header =
        $"  {"CIDR",-20} {"Кол-во IP",-12} {"Диапазон",-37} Провайдер";

    private void PrintRow(IpRange range)
    {
        var rangeStr = $"{range.MinIp} - {range.MaxIp}";
        var provider = GetProviderInfo(range.Network);
        _logger.LogResult($"  {range.Cidr,-20} {range.Count,-12} {rangeStr,-37} {provider}");
    }

    private void PrintSubnet24Table(IReadOnlyList<IpRange> ranges)
    {
        _logger.LogResult("");
        _logger.LogResult(SepDouble);
        _logger.LogResult("  Топ подсети /24 (с наибольшим количеством IP)");
        _logger.LogResult(SepDouble);
        _logger.LogResult(Header);
        _logger.LogResult(SepSingle);
        foreach (var range in ranges.Where(r => r.Cidr.EndsWith("/24")).Take(20))
            PrintRow(range);
        _logger.LogResult(SepDouble);
    }

    private void PrintSubnet16Table(IReadOnlyList<IpRange> ranges)
    {
        _logger.LogResult("");
        _logger.LogResult(SepDouble);
        _logger.LogResult("  Крупные подсети /16 (5+ IP)");
        _logger.LogResult(SepDouble);
        _logger.LogResult(Header);
        _logger.LogResult(SepSingle);
        foreach (var range in ranges.Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16))
            PrintRow(range);
        _logger.LogResult(SepDouble);
    }

    private void PrintRecommendedRanges(IReadOnlyList<IpRange> ranges)
    {
        _logger.LogResult("");
        _logger.LogResult(SepDouble);
        _logger.LogResult("  Рекомендуемые диапазоны для фильтрации");
        _logger.LogResult(SepDouble);
        var largeSubnet16 = ranges
            .Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16)
            .Select(r => ExtractSubnet16Prefix(r.Network))
            .ToHashSet();
        var recommended = ranges
            .Where(r => r.Count >= _config.MinIpCountForSubnet24)
            .Where(r => !r.Cidr.EndsWith("/24") || !largeSubnet16.Contains(ExtractSubnet16Prefix(r.Network)))
            .OrderByDescending(r => r.Count)
            .ToList();
        foreach (var range in recommended)
        {
            var provider = GetProviderInfo(range.Network);
            _logger.LogResult($"  {range.Cidr,-20} {range.Count,3} IP   {provider}");
        }
        _logger.LogResult(SepDouble);
    }

    /// <summary>
    /// Извлекает префикс подсети /16 из IP адреса (первые два октета)
    /// </summary>
    private static string ExtractSubnet16Prefix(string network)
    {
        var parts = network.Split('.');
        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }
        return network;
    }

    private string GetProviderInfo(string subnet)
    {
        var key = ExtractSubnet16Prefix(subnet);
        if (_providerCache.TryGetValue(key, out var cached))
            return cached;

        var parts = subnet.Split('.');
        if (parts.Length < 2)
            return "Unknown";

        return (parts[0], parts[1]) switch
        {
            // Yandex Cloud
            ("51", "250") or ("84", "201") or ("178", "154") or ("151", "236")
                or ("37", "18") or ("217", "16") or ("158", "160") or ("128", "75") => "Yandex Cloud",
            // Vultr
            ("45", "32") or ("45", "63") or ("64", "176") or ("66", "42")
                or ("95", "179") or ("108", "61") or ("149", "28")
                or ("155", "138") or ("207", "246") or ("216", "128") => "Vultr",
            // Hetzner
            ("5", "9") or ("5", "161") or ("23", "88") or ("65", "108") or ("65", "21")
                or ("88", "99") or ("91", "107") or ("94", "130") or ("95", "216")
                or ("128", "140") or ("135", "181") or ("136", "243") or ("138", "201")
                or ("144", "76") or ("148", "251") or ("157", "90") or ("159", "69")
                or ("162", "55") or ("168", "119") or ("176", "9") or ("178", "63")
                or ("188", "40") or ("195", "201") => "Hetzner",
            // DigitalOcean
            ("104", "131") or ("104", "236") or ("138", "197") or ("138", "68")
                or ("159", "65") or ("159", "89") or ("165", "227") or ("167", "172")
                or ("167", "99") or ("174", "138") or ("188", "166") or ("206", "189") => "DigitalOcean",
            // Linode / Akamai
            ("45", "33") or ("45", "56") or ("45", "79") or ("50", "116")
                or ("66", "175") or ("69", "164") or ("72", "14") or ("74", "207")
                or ("96", "126") or ("176", "58") or ("178", "79") or ("198", "58") => "Linode/Akamai",
            // OVH / OVHcloud
            ("51", "38") or ("51", "77") or ("51", "89") or ("51", "161")
                or ("54", "36") or ("54", "37") or ("87", "98") or ("91", "121")
                or ("92", "222") or ("94", "23") or ("135", "125") or ("137", "74")
                or ("141", "94") or ("146", "59") or ("149", "202") or ("151", "80")
                or ("158", "69") or ("188", "165") or ("193", "70") or ("195", "154") => "OVH",
            // Selectel
            ("5", "8") or ("46", "22") or ("80", "73") or ("81", "177")
                or ("85", "192") or ("85", "193") or ("85", "194") or ("85", "195")
                or ("92", "53") or ("93", "185") or ("178", "170") or ("185", "22") => "Selectel",
            // RUVDS
            ("146", "120") or ("195", "133") or ("46", "148") => "RUVDS",
            // TimeWeb Cloud
            ("109", "120") or ("185", "3") or ("91", "232") => "TimeWeb Cloud",
            // VK Cloud / Mail.ru
            ("95", "163") or ("185", "16") or ("37", "228") => "VK Cloud",
            // Contabo
            ("144", "91") or ("207", "180") or ("209", "126") => "Contabo",
            _ => "Unknown"
        };
    }

    public async Task EnrichProviderInfoAsync(IReadOnlyList<IpRange> ranges, CancellationToken cancellationToken = default)
    {
        var keysToFetch = ranges
            .Select(r => ExtractSubnet16Prefix(r.Network))
            .Distinct(StringComparer.Ordinal)
            .Where(k => !_providerCache.ContainsKey(k) && GetProviderInfo(k + ".0.0") == "Unknown")
            .ToList();

        if (keysToFetch.Count == 0) return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            for (var i = 0; i < keysToFetch.Count; i += 100)
            {
                var batch = keysToFetch.Skip(i).Take(100).ToList();
                var requestArray = new JsonArray();
                foreach (var k in batch)
                    requestArray.Add((JsonNode?)new JsonObject { ["query"] = $"{k}.1.1" });

                using var response = await http.PostAsync(
                    "http://ip-api.com/batch?fields=status,org,asname,isp,query",
                    new StringContent(requestArray.ToJsonString(), Encoding.UTF8, "application/json"),
                    cancellationToken);

                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("status", out var status) || status.GetString() != "success")
                        continue;
                    if (!entry.TryGetProperty("query", out var queryProp)) continue;
                    var query = queryProp.GetString();
                    if (query == null) continue;

                    var qParts = query.Split('.');
                    if (qParts.Length < 2) continue;
                    var key = $"{qParts[0]}.{qParts[1]}";

                    var provider =
                        (entry.TryGetProperty("org", out var org) ? org.GetString() : null)
                        ?? (entry.TryGetProperty("asname", out var asname) ? asname.GetString() : null)
                        ?? (entry.TryGetProperty("isp", out var isp) ? isp.GetString() : null)
                        ?? "";

                    if (!string.IsNullOrWhiteSpace(provider))
                        _providerCache[key] = provider;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Не удалось получить данные о провайдерах: {ex.Message}");
        }
    }
}
