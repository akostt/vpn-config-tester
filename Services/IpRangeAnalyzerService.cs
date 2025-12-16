using System.Net;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для анализа IP-диапазонов
/// </summary>
public sealed class IpRangeAnalyzerService : IIpRangeAnalyzer
{
    private readonly ApplicationConfiguration _config;
    private readonly ILogger? _logger;

    public IpRangeAnalyzerService(ApplicationConfiguration config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public IReadOnlyList<IpRange> AnalyzeIpRanges(string serversFile)
    {
        if (string.IsNullOrWhiteSpace(serversFile))
            throw new ArgumentException("Servers file path cannot be null or empty", nameof(serversFile));

        if (!File.Exists(serversFile))
        {
            _logger?.LogWarning($"Файл {serversFile} не найден");
            return Array.Empty<IpRange>();
        }

        var ipAddresses = ExtractIpAddresses(serversFile);
        
        if (ipAddresses.Count == 0)
        {
            _logger?.LogWarning("Не найдено IP-адресов для анализа");
            return Array.Empty<IpRange>();
        }

        return GroupIntoRanges(ipAddresses);
    }

    public void PrintAnalysis(IReadOnlyList<IpRange> ranges)
    {
        if (ranges == null || ranges.Count == 0)
        {
            Console.WriteLine("Нет данных для анализа");
            return;
        }

        Console.WriteLine("=== Анализ IP-диапазонов успешных серверов ===\n");
        Console.WriteLine($"Всего найдено уникальных подсетей: {ranges.Count}\n");

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

        // Получаем крупные подсети /16 (5+ IP)
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

        // Фильтруем подсети /24: исключаем те, которые входят в крупные /16
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
        _logger?.LogInfo($"Диапазоны IP сохранены в {outputFile}");
    }

    private List<IPAddress> ExtractIpAddresses(string serversFile)
    {
        var ipAddresses = new List<IPAddress>();
        var lines = File.ReadAllLines(serversFile);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var parts = trimmed.Split(':');
            if (parts.Length > 0)
            {
                var ipString = parts[0].Trim();
                if (IPAddress.TryParse(ipString, out var ipAddress))
                {
                    ipAddresses.Add(ipAddress);
                }
            }
        }

        return ipAddresses;
    }

    private List<IpRange> GroupIntoRanges(List<IPAddress> ipAddresses)
    {
        if (ipAddresses.Count == 0)
            return new List<IpRange>();

        var ranges = new List<IpRange>();

        // Группируем по /24
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

        // Группируем по /16 для больших диапазонов
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

    private string GetSubnet24(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length < 4)
            throw new ArgumentException("IPv4 address expected", nameof(ip));
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0";
    }

    private string GetSubnet16(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length < 4)
            throw new ArgumentException("IPv4 address expected", nameof(ip));
        return $"{bytes[0]}.{bytes[1]}.0.0";
    }

    private long ConvertIpToLong(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4) // IPv4
        {
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }
        // Для IPv6 используем первые 8 байт
        long result = 0;
        for (int i = 0; i < Math.Min(8, bytes.Length); i++)
        {
            result = (result << 8) | bytes[i];
        }
        return result;
    }

    private void PrintSubnet24Table(IReadOnlyList<IpRange> ranges)
    {
        Console.WriteLine("Топ подсети /24 (с наибольшим количеством IP):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"{"CIDR",-20} {"Количество IP",-15} {"Диапазон",-30} {"ASN/Провайдер",-20}");
        Console.WriteLine(new string('-', 80));

        var top24 = ranges.Where(r => r.Cidr.EndsWith("/24")).Take(20).ToList();
        foreach (var range in top24)
        {
            var provider = GetProviderInfo(range.Network);
            Console.WriteLine($"{range.Cidr,-20} {range.Count,-15} {range.MinIp} - {range.MaxIp,-20} {provider}");
        }
    }

    private void PrintSubnet16Table(IReadOnlyList<IpRange> ranges)
    {
        Console.WriteLine("\n\nКрупные подсети /16 (5+ IP):");
        Console.WriteLine(new string('-', 80));
        var large16 = ranges.Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16).ToList();
        foreach (var range in large16)
        {
            var provider = GetProviderInfo(range.Network);
            Console.WriteLine($"{range.Cidr,-20} {range.Count,-15} {range.MinIp} - {range.MaxIp,-20} {provider}");
        }
    }

    private void PrintRecommendedRanges(IReadOnlyList<IpRange> ranges)
    {
        Console.WriteLine("\n\nРекомендуемые диапазоны для фильтрации:");
        Console.WriteLine(new string('-', 80));

        // Получаем крупные подсети /16 (5+ IP)
        var largeSubnet16 = ranges
            .Where(r => r.Cidr.EndsWith("/16") && r.Count >= _config.MinIpCountForSubnet16)
            .Select(r => ExtractSubnet16Prefix(r.Network))
            .ToHashSet();

        // Фильтруем подсети /24: исключаем те, которые входят в крупные /16
        var recommended = ranges
            .Where(r => r.Count >= _config.MinIpCountForSubnet24)
            .Where(r =>
            {
                // Если это подсеть /24, проверяем, не входит ли она в крупную /16
                if (r.Cidr.EndsWith("/24"))
                {
                    var subnet16Prefix = ExtractSubnet16Prefix(r.Network);
                    return !largeSubnet16.Contains(subnet16Prefix);
                }
                // Подсети /16 всегда включаем
                return true;
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        foreach (var range in recommended)
        {
            Console.WriteLine($"CIDR: {range.Cidr} ({range.Count} IP)");
        }
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

    private static string GetProviderInfo(string subnet)
    {
        var parts = subnet.Split('.');
        if (parts.Length < 2)
            return "Unknown";

        var firstOctet = parts[0];
        var secondOctet = parts[1];

        // Определяем провайдера по известным диапазонам Yandex Cloud
        return (firstOctet, secondOctet) switch
        {
            ("51", "250") => "Yandex Cloud",
            ("84", "201") => "Yandex Cloud",
            ("178", "154") => "Yandex Cloud",
            ("151", "236") => "Yandex Cloud",
            ("37", "18") => "Yandex Cloud",
            ("217", "16") => "Yandex Cloud",
            ("158", "160") => "Yandex Cloud",
            ("128", "75") => "Yandex Cloud",
            _ => "Unknown"
        };
    }
}

