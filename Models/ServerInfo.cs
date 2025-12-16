namespace VpnConfigTester.Models;

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
    /// Оригинальная URL строка конфигурации
    /// </summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>
    /// Протокол (vless, trojan и т.д.)
    /// </summary>
    public string Protocol { get; init; } = string.Empty;

    public override int GetHashCode() => HashCode.Combine(
        Host.ToLowerInvariant(), 
        Port);

    public bool Equals(ServerInfo? other) => 
        other != null && 
        Host.Equals(other.Host, StringComparison.OrdinalIgnoreCase) && 
        Port == other.Port;
}

