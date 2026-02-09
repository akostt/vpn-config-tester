namespace VpnConfigTester.Models;

/// <summary>
/// Конфигурация приложения
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>
    /// URL(ы) для скачивания конфигурации VPN
    /// </summary>
    public string[] ConfigUrls { get; init; } = new[]
    {
        "https://gitverse.ru/api/repos/LowiK/LowiKLive/raw/branch/main/ObhodBSfree.txt",
        "https://gitverse.ru/api/repos/bywarm/rser/raw/branch/master/selected.txt",
        "https://gitverse.ru/api/repos/lolfomka/tg-WLTGFF/raw/branch/master/TG-@WLTGFF",
        "https://gitverse.ru/api/repos/Vsevj/OBwl/raw/branch/master/wwh",
        "https://raw.githubusercontent.com/EtoNeYaProject/etoneyaproject.github.io/refs/heads/main/whitelist",
        "https://raw.githubusercontent.com/gbwltg/gbwl/refs/heads/main/m2EsPqwmlc",
        "https://nowmeow.pw/8ybBd3fdCAQ6Ew5H0d66Y1hMbh63GpKUtEXQClIu/whitelist",
        "https://raw.githubusercontent.com/igareck/vpn-configs-for-russia/refs/heads/main/WHITE-CIDR-RU-checked.txt",
        "https://github.com/AvenCores/goida-vpn-configs/raw/refs/heads/main/githubmirror/26.txt",
        "https://raw.githubusercontent.com/zieng2/wl/main/vless_universal.txt",
        "https://bp.wl.free.nf/confs/merged.txt",
        "https://bp.wl.free.nf/confs/selected.txt"
    };

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
    public int MaxConcurrentTests { get; init; } = 256;

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

