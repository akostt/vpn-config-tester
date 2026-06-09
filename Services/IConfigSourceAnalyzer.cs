using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Интерфейс для анализа статистики источников конфигураций
/// </summary>
public interface IConfigSourceAnalyzer
{
    /// <summary>
    /// Анализирует все источники конфигураций и возвращает статистику
    /// </summary>
    /// <param name="allServers">Все распарсенные серверы</param>
    /// <param name="successfulServers">Успешно подключившиеся серверы</param>
    /// <returns>Список статистики по каждому источнику</returns>
    IReadOnlyList<ConfigSourceStats> AnalyzeSources(
        IReadOnlyList<ServerInfo> allServers,
        IReadOnlyList<ServerInfo> successfulServers);

    /// <summary>
    /// Выводит подробную статистику по источникам
    /// </summary>
    void PrintSourcesAnalysis(IReadOnlyList<ConfigSourceStats> stats);

    /// <summary>
    /// Рекомендует лучшие источники и их сочетания
    /// </summary>
    void RecommendBestSources(IReadOnlyList<ConfigSourceStats> stats);
}
