using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Интерфейс для парсинга серверов из конфигурации
/// </summary>
public interface IServerParser
{
    /// <summary>
    /// Парсит строки конфигурации и извлекает информацию о серверах
    /// </summary>
    /// <param name="configLines">Строки конфигурации</param>
    /// <returns>Список уникальных серверов</returns>
    IReadOnlyList<ServerInfo> ParseServers(IEnumerable<string> configLines);
}

