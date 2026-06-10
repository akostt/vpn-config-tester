using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Интерфейс для анализа IP-диапазонов
/// </summary>
public interface IIpRangeAnalyzer
{
    /// <summary>
    /// Анализирует IP-диапазоны из файла с успешными серверами
    /// </summary>
    IReadOnlyList<IpRange> AnalyzeIpRanges(string serversFile);

    /// <summary>
    /// Выводит анализ в консоль
    /// </summary>
    void PrintAnalysis(IReadOnlyList<IpRange> ranges);

    /// <summary>
    /// Сохраняет диапазоны в файл
    /// </summary>
    void SaveRangesToFile(IReadOnlyList<IpRange> ranges, string outputFile);

    /// <summary>
    /// Обогащает информацию о провайдерах через ip-api.com
    /// </summary>
    Task EnrichProviderInfoAsync(IReadOnlyList<IpRange> ranges, CancellationToken cancellationToken = default);
}

