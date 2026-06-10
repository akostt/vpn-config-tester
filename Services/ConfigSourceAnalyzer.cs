using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Сервис для анализа статистики источников конфигураций
/// </summary>
public sealed class ConfigSourceAnalyzer(ILogger? logger = null) : IConfigSourceAnalyzer
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public IReadOnlyList<ConfigSourceStats> AnalyzeSources(
        IReadOnlyList<ServerInfo> allServers,
        IReadOnlyList<ServerInfo> successfulServers)
    {
        if (allServers == null || allServers.Count == 0)
            return Array.Empty<ConfigSourceStats>();

        var successfulSet = new HashSet<string>(
            successfulServers.Select(s => $"{s.GetIpAddressOrHost()}:{s.Port}"));

        var sourceGroups = allServers
            .GroupBy(s => string.IsNullOrWhiteSpace(s.SourceConfigUrl) ? "local" : s.SourceConfigUrl);

        var stats = new List<ConfigSourceStats>();

        foreach (var group in sourceGroups)
        {
            var sourceUrl = group.Key;
            var servers = group.ToList();
            
            // Уникальные серверы по IP:Port
            var uniqueServers = servers
                .GroupBy(s => $"{s.GetIpAddressOrHost()}:{s.Port}")
                .Select(g => g.First())
                .ToList();

            // Успешные серверы из этого источника
            var successful = uniqueServers
                .Count(s => successfulSet.Contains($"{s.GetIpAddressOrHost()}:{s.Port}"));

            stats.Add(new ConfigSourceStats
            {
                SourceUrl = sourceUrl,
                TotalServers = servers.Count,
                UniqueServers = uniqueServers.Count,
                SuccessfulServers = successful
            });
        }

        return stats.OrderByDescending(s => s.QualityScore).ToList();
    }

    public void PrintSourcesAnalysis(IReadOnlyList<ConfigSourceStats> stats)
    {
        if (stats == null || stats.Count == 0)
        {
            _logger.LogWarning("Нет данных для анализа источников.");
            return;
        }

        _logger.LogInfo("");
        _logger.LogInfo("╔════════════════════════════════════════════════════════════════════════════╗");
        _logger.LogInfo("║              ПОДРОБНАЯ СТАТИСТИКА ПО ИСТОЧНИКАМ КОНФИГОВ                  ║");
        _logger.LogInfo("╚════════════════════════════════════════════════════════════════════════════╝");
        _logger.LogInfo("");

        foreach (var stat in stats)
        {
            var shortUrl = ShortenUrl(stat.SourceUrl, 70);
            
            _logger.LogInfo($"┌─ {shortUrl}");
            _logger.LogInfo($"│  Всего записей:        {stat.TotalServers}");
            _logger.LogInfo($"│  Уникальных серверов:  {stat.UniqueServers} ({stat.UniquenessPercent:F1}%)");
            _logger.LogInfo($"│  Успешных подключений: {stat.SuccessfulServers} ({stat.SuccessRatePercent:F1}%)");
            _logger.LogInfo($"│  Оценка качества:      {stat.QualityScore:F2}");
            _logger.LogInfo("");
        }
    }

    public void RecommendBestSources(IReadOnlyList<ConfigSourceStats> stats)
    {
        if (stats == null || stats.Count == 0)
            return;

        _logger.LogInfo("╔════════════════════════════════════════════════════════════════════════════╗");
        _logger.LogInfo("║                    РЕКОМЕНДАЦИИ ПО ИСТОЧНИКАМ                              ║");
        _logger.LogInfo("╚════════════════════════════════════════════════════════════════════════════╝");
        _logger.LogInfo("");

        // Топ-3 источника по качеству
        var topByQuality = stats.OrderByDescending(s => s.QualityScore).Take(3).ToList();
        _logger.LogInfo("🏆 Топ-3 источника по общему качеству:");
        for (int i = 0; i < topByQuality.Count; i++)
        {
            var stat = topByQuality[i];
            _logger.LogInfo($"  {i + 1}. {ShortenUrl(stat.SourceUrl, 65)}");
            _logger.LogInfo($"     Успешных: {stat.SuccessfulServers}, Качество: {stat.QualityScore:F2}");
        }
        _logger.LogInfo("");

        // Топ-3 по проценту успеха (только если есть хотя бы 5 успешных)
        var topBySuccessRate = stats
            .Where(s => s.SuccessfulServers >= 5)
            .OrderByDescending(s => s.SuccessRatePercent)
            .Take(3)
            .ToList();

        if (topBySuccessRate.Count > 0)
        {
            _logger.LogInfo("📊 Топ-3 источника по проценту успешных подключений:");
            for (int i = 0; i < topBySuccessRate.Count; i++)
            {
                var stat = topBySuccessRate[i];
                _logger.LogInfo($"  {i + 1}. {ShortenUrl(stat.SourceUrl, 65)}");
                _logger.LogInfo($"     Процент успеха: {stat.SuccessRatePercent:F1}% ({stat.SuccessfulServers}/{stat.UniqueServers})");
            }
            _logger.LogInfo("");
        }

        // Топ-3 по количеству успешных серверов
        var topByCount = stats
            .OrderByDescending(s => s.SuccessfulServers)
            .Take(3)
            .ToList();

        _logger.LogInfo("🔢 Топ-3 источника по количеству успешных серверов:");
        for (int i = 0; i < topByCount.Count; i++)
        {
            var stat = topByCount[i];
            _logger.LogInfo($"  {i + 1}. {ShortenUrl(stat.SourceUrl, 65)}");
            _logger.LogInfo($"     Успешных серверов: {stat.SuccessfulServers}");
        }
        _logger.LogInfo("");

        // Рекомендация оптимального сочетания
        _logger.LogInfo("💡 Рекомендуемое сочетание источников:");
        var recommended = RecommendOptimalCombination(stats);
        
        if (recommended.Count > 0)
        {
            var totalSuccessful = recommended.Sum(s => s.SuccessfulServers);
            var totalUnique = recommended.Sum(s => s.UniqueServers);
            
            _logger.LogInfo($"   Выбрано {recommended.Count} источников, которые дают {totalSuccessful} успешных серверов");
            _logger.LogInfo($"   из {totalUnique} уникальных (охват: {(totalSuccessful * 100.0 / Math.Max(totalUnique, 1)):F1}%)");
            _logger.LogInfo("");
            
            foreach (var stat in recommended)
            {
                _logger.LogInfo($"   ✓ {ShortenUrl(stat.SourceUrl, 68)}");
            }
        }
        else
        {
            _logger.LogInfo("   Используйте источники из топ-3 по качеству.");
        }
        
        _logger.LogInfo("");
    }

    private List<ConfigSourceStats> RecommendOptimalCombination(IReadOnlyList<ConfigSourceStats> stats)
    {
        // Жадный алгоритм: выбираем источники с наилучшим соотношением качества
        // Берем топ источников, пока их вклад значим
        
        var recommended = new List<ConfigSourceStats>();
        var sortedByQuality = stats.OrderByDescending(s => s.QualityScore).ToList();
        
        foreach (var stat in sortedByQuality)
        {
            // Включаем источник, если:
            // 1. У него больше 10 успешных серверов, ИЛИ
            // 2. У него процент успеха выше 30% и хотя бы 5 успешных
            if (stat.SuccessfulServers >= 10 || 
                (stat.SuccessRatePercent >= 30 && stat.SuccessfulServers >= 5))
            {
                recommended.Add(stat);
            }
            
            // Ограничим топ-5 источниками
            if (recommended.Count >= 5)
                break;
        }
        
        // Если ничего не подошло, возьмем лучший источник
        if (recommended.Count == 0 && sortedByQuality.Count > 0)
        {
            recommended.Add(sortedByQuality[0]);
        }
        
        return recommended;
    }

    private static string ShortenUrl(string url, int maxLength)
    {
        if (string.IsNullOrEmpty(url) || url.Length <= maxLength)
            return url;

        // Убираем https:// и http:// для экономии места
        var shortened = url.Replace("https://", "").Replace("http://", "");
        
        if (shortened.Length <= maxLength)
            return shortened;

        // Обрезаем с многоточием
        return shortened.Substring(0, maxLength - 3) + "...";
    }
}
