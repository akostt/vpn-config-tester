namespace VpnCheck.Services;

/// <summary>
/// Интерфейс для скачивания конфигурации VPN
/// </summary>
public interface IConfigDownloader
{
    /// <summary>
    /// Скачивает конфигурацию из указанного URL и сохраняет в файл
    /// </summary>
    /// <param name="url">URL для скачивания</param>
    /// <param name="filePath">Путь для сохранения файла</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>True если скачивание успешно, иначе False</returns>
    Task<bool> DownloadAsync(string url, string filePath, CancellationToken cancellationToken = default);
}

