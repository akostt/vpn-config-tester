using System.Text;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для записи результатов конфигурации
/// </summary>
public sealed class ConfigWriter : IConfigWriter
{
    private readonly ILogger? _logger;

    public ConfigWriter(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task SaveSuccessfulServersAsync(
        IReadOnlyList<ServerInfo> servers,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (servers == null)
            throw new ArgumentNullException(nameof(servers));
        
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        var lines = servers.Select(s => $"{s.Host}:{s.Port}").ToList();
        await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
        
        _logger?.LogInfo($"Сохранено {servers.Count} успешных серверов в {filePath}");
    }

    public async Task CreateOutputConfigAsync(
        IReadOnlyList<ServerInfo> successfulServers,
        string outputFilePath,
        IEnumerable<string> originalLines,
        CancellationToken cancellationToken = default)
    {
        if (successfulServers == null)
            throw new ArgumentNullException(nameof(successfulServers));
        
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path cannot be null or empty", nameof(outputFilePath));
        
        if (originalLines == null)
            throw new ArgumentNullException(nameof(originalLines));

        var outputLines = new List<string>();
        var successfulHosts = new HashSet<string>(
            successfulServers.Select(s => s.Host),
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in originalLines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Проверяем, содержит ли строка успешный сервер
            bool containsSuccessfulServer = successfulServers.Any(server =>
                trimmedLine.Contains(server.Host, StringComparison.OrdinalIgnoreCase) &&
                trimmedLine.Contains($":{server.Port}", StringComparison.OrdinalIgnoreCase));

            if (containsSuccessfulServer)
            {
                outputLines.Add(trimmedLine);
            }
        }

        await File.WriteAllLinesAsync(outputFilePath, outputLines, Encoding.UTF8, cancellationToken);
        
        _logger?.LogInfo($"Создан выходной конфиг с {outputLines.Count} строками в {outputFilePath}");
    }
}

