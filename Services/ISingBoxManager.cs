namespace VpnCheck.Services;

/// <summary>
/// Интерфейс для управления бинарником sing-box
/// </summary>
public interface ISingBoxManager
{
    /// <summary>
    /// Гарантирует наличие исполняемого файла sing-box и возвращает путь
    /// </summary>
    /// <returns>Путь к бинарнику или null, если недоступен</returns>
    Task<string?> EnsureSingBoxAsync(CancellationToken cancellationToken = default);
}
