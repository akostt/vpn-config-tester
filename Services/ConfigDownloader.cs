using System.Text;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

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

        try
        {
            logger?.LogInfo($"Скачивание конфига из {url}...");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_config.HttpTimeoutSeconds);

            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);

            logger?.LogInfo($"Конфиг успешно скачан и сохранен в {filePath}");
            return true;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError($"Ошибка HTTP при скачивании: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            logger?.LogError($"Таймаут при скачивании: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogError($"Ошибка при скачивании: {ex.Message}");
            return false;
        }
    }
}

