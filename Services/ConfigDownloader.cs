using System.Text;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

public sealed class ConfigDownloader(
    HttpClient httpClient,
    ApplicationConfiguration config,
    ILogger? logger = null) : IConfigDownloader
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<bool> DownloadAsync(string url, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        // Only allow http/https to prevent unexpected scheme abuse (file://, ftp://, etc.)
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning($"Отклонён URL с недопустимой схемой: {url}");
            return false;
        }

        if (parsedUri.Scheme == Uri.UriSchemeHttp)
            _logger.LogWarning($"Небезопасное подключение (HTTP без шифрования): {url}");

        try
        {
            _logger.LogInfo($"Скачивание конфига из {url}...");

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);

            _logger.LogSuccess($"Конфиг скачан: {url}");
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning($"Ошибка HTTP [{url}]: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning($"Таймаут [{url}]");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Ошибка скачивания [{url}]: {ex.Message}");
            return false;
        }
    }
}
