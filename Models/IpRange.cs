using System.Net;

namespace VpnConfigTester.Models;

/// <summary>
/// Представляет диапазон IP-адресов
/// </summary>
public sealed class IpRange
{
    /// <summary>
    /// Сетевая маска (например, "192.168.1.0")
    /// </summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>
    /// Минимальный IP адрес в диапазоне
    /// </summary>
    public IPAddress MinIp { get; set; } = IPAddress.None;

    /// <summary>
    /// Максимальный IP адрес в диапазоне
    /// </summary>
    public IPAddress MaxIp { get; set; } = IPAddress.None;

    /// <summary>
    /// Количество IP адресов в диапазоне
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// CIDR нотация (например, "192.168.1.0/24")
    /// </summary>
    public string Cidr { get; set; } = string.Empty;
}

