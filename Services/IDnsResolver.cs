using System.Net;

namespace VpnCheck.Services;

/// <summary>
/// Интерфейс для резолва доменных имен в IP адреса
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    /// Резолвит доменное имя в IP адрес (возвращает только один IPv4 адрес, как socket.gethostbyname в Python)
    /// </summary>
    /// <param name="hostname">Доменное имя или IP адрес</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>IP адрес или null, если резолв не удался</returns>
    Task<IPAddress?> ResolveAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Резолвит список доменных имен в IP адреса
    /// </summary>
    /// <param name="hostnames">Список доменных имен</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Словарь: доменное имя -> IP адрес</returns>
    Task<Dictionary<string, IPAddress?>> ResolveBatchAsync(
        IEnumerable<string> hostnames,
        Action<int, int, int>? onProgress = null,
        CancellationToken cancellationToken = default);
}

