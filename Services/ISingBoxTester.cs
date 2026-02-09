using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Интерфейс для проверки конфигов через sing-box
/// </summary>
public interface ISingBoxTester
{
    /// <summary>
    /// Выполняет проверку конфигов через sing-box и возвращает успешные
    /// </summary>
    Task<IReadOnlyList<ServerInfo>> TestAsync(
        IReadOnlyList<ServerInfo> servers,
        string singBoxPath,
        CancellationToken cancellationToken = default);
}
