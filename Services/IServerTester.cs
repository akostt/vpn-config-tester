using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Интерфейс для тестирования доступности серверов
/// </summary>
public interface IServerTester
{
    /// <summary>
    /// Тестирует доступность серверов через TCP подключение
    /// </summary>
    /// <param name="servers">Список серверов для тестирования</param>
    /// <param name="progressCallback">Callback для отслеживания прогресса</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список доступных серверов</returns>
    Task<IReadOnlyList<ServerInfo>> TestServersAsync(
        IReadOnlyList<ServerInfo> servers,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

