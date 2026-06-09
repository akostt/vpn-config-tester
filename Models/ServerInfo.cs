using System.Net;

namespace VpnCheck.Models;

/// <summary>
/// Представляет информацию о VPN сервере
/// </summary>
public sealed record ServerInfo
{
    /// <summary>
    /// Хост (IP адрес или доменное имя)
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// Порт сервера
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Резолвленный IP адрес (если Host был доменным именем)
    /// </summary>
    public IPAddress? ResolvedIpAddress { get; init; }

    /// <summary>
    /// Оригинальная URL строка конфигурации
    /// </summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>
    /// URL исходного файла конфигурации, из которого была получена эта запись
    /// </summary>
    public string SourceConfigUrl { get; init; } = string.Empty;

    /// <summary>
    /// Протокол (vless, trojan и т.д.)
    /// </summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>
    /// Возвращает IP адрес для использования (резолвленный или оригинальный Host, если это IP)
    /// </summary>
    public string GetIpAddressOrHost() =>
        ResolvedIpAddress?.ToString() ?? Host;

    // Equality is intentionally based on (Host, Port) only — two configs pointing
    // to the same endpoint are treated as one unit for TCP-reachability purposes.
    // Protocol and credential differences are handled at a higher level (URL-based dedup in Application.cs).
    public override int GetHashCode() => HashCode.Combine(
        Host.ToLowerInvariant(),
        Port);

    public bool Equals(ServerInfo? other) => 
        other != null && 
        Host.Equals(other.Host, StringComparison.OrdinalIgnoreCase) && 
        Port == other.Port;
}

