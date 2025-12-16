using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Интерфейс для записи результатов конфигурации
/// </summary>
public interface IConfigWriter
{
    /// <summary>
    /// Сохраняет список успешных серверов в файл
    /// </summary>
    Task SaveSuccessfulServersAsync(IReadOnlyList<ServerInfo> servers, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создает выходной конфиг только с успешными серверами
    /// </summary>
    Task CreateOutputConfigAsync(
        IReadOnlyList<ServerInfo> successfulServers,
        string outputFilePath,
        IEnumerable<string> originalLines,
        CancellationToken cancellationToken = default);
}

