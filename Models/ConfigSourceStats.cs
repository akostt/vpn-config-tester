namespace VpnCheck.Models;

/// <summary>
/// Статистика по источнику конфигурации
/// </summary>
public sealed class ConfigSourceStats
{
    /// <summary>
    /// URL источника
    /// </summary>
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Общее количество серверов из этого источника
    /// </summary>
    public int TotalServers { get; init; }

    /// <summary>
    /// Количество уникальных серверов (по IP:Port)
    /// </summary>
    public int UniqueServers { get; init; }

    /// <summary>
    /// Количество успешно подключившихся серверов
    /// </summary>
    public int SuccessfulServers { get; init; }

    /// <summary>
    /// Процент уникальных серверов от общего числа
    /// </summary>
    public double UniquenessPercent => TotalServers > 0 ? (UniqueServers * 100.0 / TotalServers) : 0;

    /// <summary>
    /// Процент успешных подключений
    /// </summary>
    public double SuccessRatePercent => UniqueServers > 0 ? (SuccessfulServers * 100.0 / UniqueServers) : 0;

    /// <summary>
    /// Оценка качества источника (комбинированный показатель)
    /// </summary>
    public double QualityScore => CalculateQualityScore();

    private double CalculateQualityScore()
    {
        // Формула: (успешные серверы * 2) + (процент успеха / 10) + (процент уникальности / 20)
        // Приоритет: количество успешных, затем процент успеха, затем уникальность
        return (SuccessfulServers * 2.0) + (SuccessRatePercent / 10.0) + (UniquenessPercent / 20.0);
    }
}
