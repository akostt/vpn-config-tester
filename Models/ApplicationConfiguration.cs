namespace VpnConfigTester.Models;

/// <summary>
/// Конфигурация приложения
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>
    /// URL для скачивания конфигурации VPN
    /// </summary>
    public string ConfigUrl { get; init; } = 
        "https://raw.githubusercontent.com/akostt/goida-vpn-configs/refs/heads/main/githubmirror/26.txt";

    /// <summary>
    /// Имя файла для сохранения исходной конфигурации
    /// </summary>
    public string SourceConfigFile { get; init; } = "source_config.txt";

    /// <summary>
    /// Имя файла для сохранения успешных серверов
    /// </summary>
    public string SuccessfulServersFile { get; init; } = "successful_servers.txt";

    /// <summary>
    /// Имя файла для сохранения выходного конфига
    /// </summary>
    public string OutputConfigFile { get; init; } = "output_config.txt";

    /// <summary>
    /// Имя файла для сохранения IP диапазонов
    /// </summary>
    public string IpRangesFile { get; init; } = "ip_ranges.txt";

    /// <summary>
    /// Таймаут TCP подключения в миллисекундах
    /// </summary>
    public int TcpTimeoutMs { get; init; } = 3000;

    /// <summary>
    /// Максимальное количество одновременных тестов
    /// </summary>
    public int MaxConcurrentTests { get; init; } = 50;

    /// <summary>
    /// Таймаут HTTP запроса в секундах
    /// </summary>
    public int HttpTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Минимальное количество IP для включения подсети /24 в рекомендации
    /// </summary>
    public int MinIpCountForSubnet24 { get; init; } = 3;

    /// <summary>
    /// Минимальное количество IP для включения подсети /16 в рекомендации
    /// </summary>
    public int MinIpCountForSubnet16 { get; init; } = 5;
}

