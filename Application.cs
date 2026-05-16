using System.Text;
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
    IConfigSourceAnalyzer configSourceAnalyzer,
    ISingBoxManager singBoxManager,
    ISingBoxTester singBoxTester,
    IDnsResolver dnsResolver,
    ILogger logger)
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IConfigDownloader _configDownloader = configDownloader ?? throw new ArgumentNullException(nameof(configDownloader));
    private readonly IServerParser _serverParser = serverParser ?? throw new ArgumentNullException(nameof(serverParser));
    private readonly IServerTester _serverTester = serverTester ?? throw new ArgumentNullException(nameof(serverTester));
    private readonly IConfigWriter _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
    private readonly IIpRangeAnalyzer _ipRangeAnalyzer = ipRangeAnalyzer ?? throw new ArgumentNullException(nameof(ipRangeAnalyzer));
    private readonly IConfigSourceAnalyzer _configSourceAnalyzer = configSourceAnalyzer ?? throw new ArgumentNullException(nameof(configSourceAnalyzer));
    private readonly ISingBoxManager _singBoxManager = singBoxManager ?? throw new ArgumentNullException(nameof(singBoxManager));
    private readonly ISingBoxTester _singBoxTester = singBoxTester ?? throw new ArgumentNullException(nameof(singBoxTester));
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

        List<(string Line, string SourceUrl)> combinedLines = new();

        if (skipDownload)
        {
            _logger.LogInfo($"Режим локального файла: используется существующий {_config.SourceConfigFile}");

            if (!File.Exists(_config.SourceConfigFile))
            {
                _logger.LogError($"Файл {_config.SourceConfigFile} не найден!");
                _logger.LogError("Создайте файл или запустите без флага --skip-download для загрузки конфигурации.");
                return;
            }

            var content = await File.ReadAllTextAsync(_config.SourceConfigFile, cancellationToken);
            AddConfigContent(combinedLines, content, "local");
        }
        else
        {
            _logger.LogInfo("Скачивание конфигураций из указанных URL...");
            var index = 0;
            var anyDownloaded = false;

            foreach (var url in _config.ConfigUrls ?? Array.Empty<string>())
            {
                index++;
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                var tempFile = $"{_config.SourceConfigFile}.download.{index}.tmp";
                var success = await _configDownloader.DownloadAsync(url, tempFile, cancellationToken);
                if (!success)
                {
                    _logger.LogWarning($"Игнорируется URL (не удалось скачать): {url}");
                    continue;
                }

                anyDownloaded = true;
                var content = await File.ReadAllTextAsync(tempFile, cancellationToken);
                AddConfigContent(combinedLines, content, url);
            }

            if (!anyDownloaded)
            {
                if (File.Exists(_config.SourceConfigFile))
                {
                    _logger.LogWarning("Не удалось скачать ни одного конфига. Будет использован существующий локальный файл.");
                    var content = await File.ReadAllTextAsync(_config.SourceConfigFile, cancellationToken);
                    AddConfigContent(combinedLines, content, "local");
                }
                else
                {
                    _logger.LogError("Не удалось скачать ни одного конфига и локальный файл отсутствует. Прекращаю работу.");
                    return;
                }
            }

            // Сохраняем объединённый исходный файл для совместимости с остальной логикой
            try
            {
                var allLines = combinedLines.Select(x => x.Line).ToArray();
                await File.WriteAllLinesAsync(_config.SourceConfigFile, allLines, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Не удалось сохранить объединённый файл {_config.SourceConfigFile}: {ex.Message}");
            }
        }

        var singBoxPath = await _singBoxManager.EnsureSingBoxAsync(cancellationToken);

        WaitForUserConfirmation();

        var servers = await LoadAndParseConfigAsync(combinedLines, cancellationToken);
        if (servers.Count == 0)
        {
            _logger.LogError("Не найдено серверов для тестирования");
            return;
        }

        var serversWithResolvedIp = await ResolveAllHostnamesAsync(servers, cancellationToken);
        var successfulServers = await TestUniqueIpsAndMapToConfigsAsync(serversWithResolvedIp, cancellationToken);
        var finalSuccessfulServers = successfulServers;

        if (!string.IsNullOrWhiteSpace(singBoxPath) && successfulServers.Count > 0)
        {
            if (PromptYesNo("TCP ping завершен. Выполнить дополнительную проверку через sing-box? (y/n): "))
            {
                var singBoxSuccessful = await _singBoxTester.TestAsync(successfulServers, singBoxPath, cancellationToken);
                finalSuccessfulServers = singBoxSuccessful;
            }
        }
        else if (string.IsNullOrWhiteSpace(singBoxPath))
        {
            _logger.LogWarning("sing-box недоступен, дополнительная проверка пропущена.");
        }
        
        // Анализ и рекомендации по источникам конфигов
        if (finalSuccessfulServers.Count > 0 && servers.Count > 0)
        {
            var sourceStats = _configSourceAnalyzer.AnalyzeSources(serversWithResolvedIp, finalSuccessfulServers);
            _configSourceAnalyzer.PrintSourcesAnalysis(sourceStats);
            _configSourceAnalyzer.RecommendBestSources(sourceStats);
        }

        await SaveResultsAsync(finalSuccessfulServers, cancellationToken);
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

    private bool PromptYesNo(string message)
    {
        try
        {
            Console.Write(message);
            var key = Console.ReadKey(true);
            Console.WriteLine();
            return key.KeyChar == 'y' || key.KeyChar == 'Y' || key.KeyChar == 'д' || key.KeyChar == 'Д';
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<Models.ServerInfo>> LoadAndParseConfigAsync(
        IEnumerable<(string Line, string SourceUrl)> combinedLines,
        CancellationToken cancellationToken)
    {
        var lines = combinedLines?.Where(x => !string.IsNullOrWhiteSpace(x.Line)).ToList() ?? new();
        _logger.LogInfo($"Загружено {lines.Count} строк конфигурации из {lines.Select(x=>x.SourceUrl).Distinct().Count()} источников.");
        _logger.LogInfo("Извлечение серверов...");

        var servers = _serverParser.ParseServers(lines);
        _logger.LogInfo($"Найдено {servers.Count} серверов для тестирования.");
        _logger.LogInfo("");

        return servers;
    }

    private void AddConfigContent(List<(string Line, string SourceUrl)> combinedLines, string content, string sourceUrl)
    {
        if (combinedLines == null)
            throw new ArgumentNullException(nameof(combinedLines));

        var normalizedContent = NormalizeConfigContent(content, sourceUrl);
        if (string.IsNullOrWhiteSpace(normalizedContent))
            return;

        var lines = normalizedContent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var line in lines)
            combinedLines.Add((line, sourceUrl));
    }

    private string NormalizeConfigContent(string content, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var trimmed = content.Trim();
        if (ContainsConfigScheme(trimmed))
            return content;

        if (!TryDecodeBase64Config(trimmed, out var decoded))
            return content;

        _logger.LogInfo(string.IsNullOrWhiteSpace(sourceUrl)
            ? "Обнаружен base64-encoded конфиг, выполняю расшифровку."
            : $"Обнаружен base64-encoded конфиг из {sourceUrl}, выполняю расшифровку.");

        return decoded;
    }

    private static bool TryDecodeBase64Config(string content, out string decoded)
    {
        decoded = string.Empty;

        var normalized = NormalizeBase64Input(content);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 32)
            return false;

        if (!IsLikelyBase64(normalized))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(PadBase64(normalized));
            decoded = Encoding.UTF8.GetString(bytes).Trim();

            return !string.IsNullOrWhiteSpace(decoded) && ContainsConfigScheme(decoded);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeBase64Input(string content)
    {
        return new string(content
            .Where(ch => !char.IsWhiteSpace(ch))
            .Select(ch => ch == '-' ? '+' : ch == '_' ? '/' : ch)
            .ToArray());
    }

    private static string PadBase64(string content)
    {
        var pad = content.Length % 4;
        return pad == 0 ? content : content.PadRight(content.Length + (4 - pad), '=');
    }

    private static bool IsLikelyBase64(string content)
    {
        foreach (var ch in content)
        {
            if (char.IsLetterOrDigit(ch) || ch == '+' || ch == '/' || ch == '=')
                continue;

            return false;
        }

        return content.Length % 4 != 1;
    }

    private static bool ContainsConfigScheme(string content)
    {
        return content.Contains("vless://", StringComparison.OrdinalIgnoreCase)
            || content.Contains("trojan://", StringComparison.OrdinalIgnoreCase)
            || content.Contains("vmess://", StringComparison.OrdinalIgnoreCase)
            || content.Contains("ss://", StringComparison.OrdinalIgnoreCase)
            || content.Contains("hysteria2://", StringComparison.OrdinalIgnoreCase)
            || content.Contains("hysteria://", StringComparison.OrdinalIgnoreCase);
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
        _logger.LogInfo("Группировка по уникальным IP адресам и портам...");

        var serversWithIp = servers
            .Select(s => new { Server = s, Ip = s.GetIpAddressOrHost() })
            .Where(x => System.Net.IPAddress.TryParse(x.Ip, out _))
            .ToList();

        var uniqueIps = serversWithIp
            .Select(x => x.Ip)
            .Distinct()
            .ToList();

        _logger.LogInfo($"Найдено {uniqueIps.Count} уникальных IP адресов из {serversWithIp.Count} серверов");
        
        _logger.LogInfo("");
        _logger.LogInfo("Группировка уникальных конфигов по параметрам подключения...");
        
        var uniqueConfigsByEndpoint = serversWithIp
            .GroupBy(x => ServerTester.BuildEndpointKey(x.Ip, x.Server.Port), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => DeduplicateByConnectionParameters(g.Select(x => x.Server).ToList()),
                StringComparer.OrdinalIgnoreCase);

        var totalUniqueConfigs = uniqueConfigsByEndpoint.Values.Sum(list => list.Count);
        _logger.LogInfo($"Всего уникальных конфигов: {totalUniqueConfigs} (по параметрам подключения, без учета названий)");

        _logger.LogInfo("");
        var uniqueEndpoints = serversWithIp
            .Select(x => (IpAddress: x.Ip, x.Server.Port))
            .Distinct()
            .ToList();

        _logger.LogInfo($"Начинаю TCP тестирование {uniqueEndpoints.Count} уникальных IP:port...");

        var successfulEndpoints = await _serverTester.TestUniqueEndpointsAsync(
            uniqueEndpoints,
            (tested, total, successful) => _progressReporter.Report(tested, total, successful),
            cancellationToken);

        var allSuccessfulServers = successfulEndpoints
            .Where(uniqueConfigsByEndpoint.ContainsKey)
            .SelectMany(endpoint => uniqueConfigsByEndpoint[endpoint])
            .ToList();

        _logger.LogInfo("");
        _logger.LogInfo($"TCP тестирование завершено. Успешных IP:port: {successfulEndpoints.Count} из {uniqueEndpoints.Count}");
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
