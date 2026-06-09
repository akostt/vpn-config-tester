using System.Text;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Реализация сервиса для скачивания конфигурации VPN
/// </summary>
public sealed class ConfigDownloader(ApplicationConfiguration config, ILogger? logger = null) : IConfigDownloader
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Скачивает конфигурацию из указанного URL и сохраняет в файл
    /// </summary>
    public async Task<bool> DownloadAsync(string url, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            logger?.LogWarning($"Небезопасное подключение (HTTP без шифрования): {url}");

        try
        {
            logger?.LogInfo($"Скачивание конфига из {url}...");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_config.HttpTimeoutSeconds);

            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);

            logger?.LogSuccess($"Конфиг скачан: {url}");
            return true;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning($"Ошибка HTTP [{url}]: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException)
        {
            logger?.LogWarning($"Таймаут [{url}]");
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка скачивания [{url}]: {ex.Message}");
            return false;
        }
    }
}

