using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;
using VpnConfigTester.Services;

namespace VpnConfigTester;

/// <summary>
/// Главный класс приложения, координирующий работу всех сервисов
/// </summary>
public sealed class Application(
    ApplicationConfiguration config,
    IConfigDownloader configDownloader,
    IServerParser serverParser,
    IServerTester serverTester,
    IConfigWriter configWriter,
    IIpRangeAnalyzer ipRangeAnalyzer,
    IDnsResolver dnsResolver,
    ILogger logger)
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IConfigDownloader _configDownloader = configDownloader ?? throw new ArgumentNullException(nameof(configDownloader));
    private readonly IServerParser _serverParser = serverParser ?? throw new ArgumentNullException(nameof(serverParser));
    private readonly IServerTester _serverTester = serverTester ?? throw new ArgumentNullException(nameof(serverTester));
    private readonly IConfigWriter _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
    private readonly IIpRangeAnalyzer _ipRangeAnalyzer = ipRangeAnalyzer ?? throw new ArgumentNullException(nameof(ipRangeAnalyzer));
    private readonly IDnsResolver _dnsResolver = dnsResolver ?? throw new ArgumentNullException(nameof(dnsResolver));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConsoleProgressReporter _progressReporter = new();

    /// <summary>
    /// Запускает основной процесс тестирования VPN серверов
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("=== VPN Config Tester ===");
        _logger.LogInfo("");

        var downloadSuccess = await _configDownloader.DownloadAsync(
            _config.ConfigUrl,
            _config.SourceConfigFile,
            cancellationToken);

        if (!downloadSuccess)
        {
            _logger.LogWarning($"Не удалось скачать конфиг. Будет использован файл {_config.SourceConfigFile}, если он существует.");
        }

        WaitForUserConfirmation();

        var servers = await LoadAndParseConfigAsync(cancellationToken);
        if (servers.Count == 0)
        {
            _logger.LogError("Не найдено серверов для тестирования");
            return;
        }

        var successfulServers = await TestServersAsync(servers, cancellationToken);
        var serversWithResolvedIp = await ResolveHostnamesAsync(successfulServers, cancellationToken);
        await SaveResultsAsync(serversWithResolvedIp, cancellationToken);
        await AnalyzeIpRangesAsync(cancellationToken);
    }

    /// <summary>
    /// Запускает анализ существующих данных
    /// </summary>
    public void AnalyzeExistingData()
    {
        if (!File.Exists(_config.SuccessfulServersFile))
        {
            _logger.LogError($"Файл {_config.SuccessfulServersFile} не найден!");
            return;
        }

        _logger.LogInfo("=== Анализ IP-диапазонов из успешных серверов ===\n");
        var ipRanges = _ipRangeAnalyzer.AnalyzeIpRanges(_config.SuccessfulServersFile);

        if (ipRanges.Count > 0)
        {
            _ipRangeAnalyzer.PrintAnalysis(ipRanges);
            _ipRangeAnalyzer.SaveRangesToFile(ipRanges, _config.IpRangesFile);
            _logger.LogInfo($"\nДиапазоны IP сохранены в: {_config.IpRangesFile}");
        }
        else
        {
            _logger.LogWarning("Не найдено IP-адресов для анализа.");
        }
    }

    private void WaitForUserConfirmation()
    {
        _logger.LogInfo("");
        _logger.LogInfo("Нажмите любую клавишу для начала тестирования серверов...");
        try
        {
            Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // Консоль недоступна, продолжаем
        }
        _logger.LogInfo("");
    }

    private async Task<IReadOnlyList<Models.ServerInfo>> LoadAndParseConfigAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_config.SourceConfigFile))
        {
            throw new FileNotFoundException($"Файл {_config.SourceConfigFile} не найден!");
        }

        var configContent = await File.ReadAllTextAsync(_config.SourceConfigFile, cancellationToken);
        var configLines = configContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        _logger.LogInfo($"Загружено {configLines.Length} строк конфигурации.");
        _logger.LogInfo("Извлечение серверов...");

        var servers = _serverParser.ParseServers(configLines);
        _logger.LogInfo($"Найдено {servers.Count} уникальных серверов для тестирования.");
        _logger.LogInfo("");

        return servers;
    }

    private async Task<IReadOnlyList<Models.ServerInfo>> TestServersAsync(
        IReadOnlyList<Models.ServerInfo> servers,
        CancellationToken cancellationToken)
    {
        _logger.LogInfo("Начинаю тестирование серверов (TCP ping)...");

        var successfulServers = await _serverTester.TestServersAsync(
            servers,
            (tested, total, successful) => _progressReporter.Report(tested, total, successful),
            cancellationToken);

        _logger.LogInfo("");
        _logger.LogInfo($"Тестирование завершено. Успешных серверов: {successfulServers.Count} из {servers.Count}");

        return successfulServers;
    }

    private async Task<IReadOnlyList<Models.ServerInfo>> ResolveHostnamesAsync(
        IReadOnlyList<Models.ServerInfo> servers,
        CancellationToken cancellationToken)
    {
        if (servers.Count == 0)
            return servers;

        _logger.LogInfo("");
        _logger.LogInfo("Резолв доменных имен в IP адреса...");

        var hostnamesToResolve = servers
            .Select(s => s.Host)
            .Where(IsHostname)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hostnamesToResolve.Count == 0)
        {
            _logger.LogInfo("Все серверы уже имеют IP адреса, резолв не требуется.");
            return servers;
        }

        _logger.LogInfo($"Резолв {hostnamesToResolve.Count} уникальных доменных имен...");
        var resolvedIps = await _dnsResolver.ResolveBatchAsync(hostnamesToResolve, cancellationToken);

        var serversWithResolvedIp = servers
            .Select(server => resolvedIps.TryGetValue(server.Host, out var resolvedIp)
                ? server with { ResolvedIpAddress = resolvedIp }
                : server)
            .ToList();

        var resolvedCount = resolvedIps.Values.Count(ip => ip != null);
        _logger.LogInfo($"Резолв завершен: {resolvedCount} из {hostnamesToResolve.Count} доменных имен успешно резолвлены.");

        return serversWithResolvedIp;
    }

    private static bool IsHostname(string host) => !System.Net.IPAddress.TryParse(host, out _);

    private async Task SaveResultsAsync(
        IReadOnlyList<Models.ServerInfo> successfulServers,
        CancellationToken cancellationToken)
    {
        if (successfulServers.Count == 0)
        {
            _logger.LogWarning("Не найдено ни одного доступного сервера.");
            return;
        }

        await _configWriter.SaveSuccessfulServersAsync(
            successfulServers,
            _config.SuccessfulServersFile,
            cancellationToken);

        var originalLines = await File.ReadAllLinesAsync(_config.SourceConfigFile, cancellationToken);
        await _configWriter.CreateOutputConfigAsync(
            successfulServers,
            _config.OutputConfigFile,
            originalLines,
            cancellationToken);

        _logger.LogInfo($"Результаты сохранены:");
        _logger.LogInfo($"  - Список серверов: {_config.SuccessfulServersFile}");
        _logger.LogInfo($"  - Конфиг: {_config.OutputConfigFile}");
    }

    private Task AnalyzeIpRangesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("");
        _logger.LogInfo("Анализ IP-диапазонов...");

        var ipRanges = _ipRangeAnalyzer.AnalyzeIpRanges(_config.SuccessfulServersFile);

        if (ipRanges.Count > 0)
        {
            _ipRangeAnalyzer.PrintAnalysis(ipRanges);
            _ipRangeAnalyzer.SaveRangesToFile(ipRanges, _config.IpRangesFile);
            _logger.LogInfo($"\nДиапазоны IP сохранены в: {_config.IpRangesFile}");
        }

        return Task.CompletedTask;
    }
}

