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
    /// <param name="skipDownload">Если true, пропускает загрузку и использует существующий файл</param>
    /// <param name="cancellationToken">Токен отмены операции</param>
    public async Task RunAsync(bool skipDownload = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("=== VPN Config Tester ===");
        _logger.LogInfo("");

        if (skipDownload)
        {
            _logger.LogInfo($"Режим локального файла: используется существующий {_config.SourceConfigFile}");
            
            if (!File.Exists(_config.SourceConfigFile))
            {
                _logger.LogError($"Файл {_config.SourceConfigFile} не найден!");
                _logger.LogError("Создайте файл или запустите без флага --skip-download для загрузки конфигурации.");
                return;
            }
        }
        else
        {
            var downloadSuccess = await _configDownloader.DownloadAsync(
                _config.ConfigUrl,
                _config.SourceConfigFile,
                cancellationToken);

            if (!downloadSuccess)
            {
                _logger.LogWarning($"Не удалось скачать конфиг. Будет использован файл {_config.SourceConfigFile}, если он существует.");
            }
        }

        WaitForUserConfirmation();

        var servers = await LoadAndParseConfigAsync(cancellationToken);
        if (servers.Count == 0)
        {
            _logger.LogError("Не найдено серверов для тестирования");
            return;
        }

        var serversWithResolvedIp = await ResolveAllHostnamesAsync(servers, cancellationToken);
        var successfulServers = await TestUniqueIpsAndMapToConfigsAsync(serversWithResolvedIp, cancellationToken);
        await SaveResultsAsync(successfulServers, cancellationToken);
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
        _logger.LogInfo($"Найдено {servers.Count} серверов для тестирования.");
        _logger.LogInfo("");

        return servers;
    }

    private async Task<IReadOnlyList<Models.ServerInfo>> ResolveAllHostnamesAsync(
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

    private async Task<IReadOnlyList<Models.ServerInfo>> TestUniqueIpsAndMapToConfigsAsync(
        IReadOnlyList<Models.ServerInfo> servers,
        CancellationToken cancellationToken)
    {
        _logger.LogInfo("");
        _logger.LogInfo("Группировка по уникальным IP адресам...");

        var serversWithIp = servers
            .Select(s => new { Server = s, Ip = s.GetIpAddressOrHost() })
            .Where(x => System.Net.IPAddress.TryParse(x.Ip, out _))
            .ToList();

        var uniqueIps = serversWithIp
            .Select(x => x.Ip)
            .Distinct()
            .ToList();

        _logger.LogInfo($"Найдено {uniqueIps.Count} уникальных IP адресов из {serversWithIp.Count} серверов");
        
        var serversByIp = serversWithIp
            .GroupBy(x => x.Ip)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Server).ToList());

        _logger.LogInfo("");
        _logger.LogInfo("Группировка уникальных конфигов по параметрам подключения...");
        
        var uniqueConfigsByIp = new Dictionary<string, List<ServerInfo>>();
        foreach (var kvp in serversByIp)
        {
            var uniqueConfigs = DeduplicateByConnectionParameters(kvp.Value);
            uniqueConfigsByIp[kvp.Key] = uniqueConfigs;
        }

        var totalUniqueConfigs = uniqueConfigsByIp.Values.Sum(list => list.Count);
        _logger.LogInfo($"Всего уникальных конфигов: {totalUniqueConfigs} (по параметрам подключения, без учета названий)");

        _logger.LogInfo("");
        _logger.LogInfo($"Начинаю ICMP ping тестирование {uniqueIps.Count} уникальных IP адресов...");

        var successfulIps = await _serverTester.TestUniqueIpsAsync(
            uniqueIps,
            (tested, total, successful) => _progressReporter.Report(tested, total, successful),
            cancellationToken);

        var allSuccessfulServers = successfulIps
            .Where(ip => uniqueConfigsByIp.ContainsKey(ip))
            .SelectMany(ip => uniqueConfigsByIp[ip])
            .ToList();

        _logger.LogInfo("");
        _logger.LogInfo($"Ping завершен. Успешных IP адресов: {successfulIps.Count} из {uniqueIps.Count}");
        _logger.LogInfo($"Успешных уникальных конфигов: {allSuccessfulServers.Count}");

        return allSuccessfulServers;
    }

    private List<ServerInfo> DeduplicateByConnectionParameters(List<ServerInfo> servers)
    {
        var uniqueConfigs = new List<ServerInfo>();
        var seenUrls = new HashSet<string>();

        foreach (var server in servers)
        {
            var normalizedUrl = NormalizeUrlForComparison(server.OriginalUrl);
            
            if (seenUrls.Add(normalizedUrl))
            {
                uniqueConfigs.Add(server);
            }
        }

        return uniqueConfigs;
    }

    private string NormalizeUrlForComparison(string url)
    {
        var hashIndex = url.IndexOf('#');
        return hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
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

