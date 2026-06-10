using Spectre.Console;
using VpnCheck.Infrastructure;
using VpnCheck.Models;
using VpnCheck.Services;

namespace VpnCheck;

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

    public async Task RunAsync(bool skipDownload = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("=== VPNCheck ===");
        _logger.LogInfo("");

        var singBoxPath = _config.SingBoxEnabled
            ? await _singBoxManager.EnsureSingBoxAsync(cancellationToken)
            : null;

        List<(string Line, string SourceUrl)> combinedLines = new();
        IReadOnlyList<Models.ServerInfo> servers = Array.Empty<Models.ServerInfo>();
        IReadOnlyList<Models.ServerInfo> serversWithResolvedIp = Array.Empty<Models.ServerInfo>();
        IReadOnlyList<Models.ServerInfo> successfulServers = Array.Empty<Models.ServerInfo>();
        IReadOnlyList<Models.ServerInfo> finalSuccessfulServers = Array.Empty<Models.ServerInfo>();
        List<string> failedDownloads = new();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .StartAsync(async ctx =>
            {
                // ── Download ──────────────────────────────────────────────
                if (skipDownload)
                {
                    if (!File.Exists(_config.SourceConfigFile))
                    {
                        _logger.LogError($"Файл {_config.SourceConfigFile} не найден!");
                        return;
                    }
                    var content = await File.ReadAllTextAsync(_config.SourceConfigFile, cancellationToken);
                    AddConfigContent(combinedLines, content, "local");
                }
                else
                {
                    var urls = _config.SubscriptionUrls
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToArray();

                    var dlTask = ctx.AddTask(
                        $"[cyan]Загрузка конфигов[/]  [grey]0/{urls.Length}[/]",
                        maxValue: Math.Max(1, urls.Length));
                    int dlDone = 0, dlSuccess = 0;
                    var downloadResults = new (string Url, string? Content, bool Success)[urls.Length];

                    await Task.WhenAll(urls.Select(async (url, i) =>
                    {
                        downloadResults[i] = await DownloadOneAsync(url, i, cancellationToken);
                        if (downloadResults[i].Success) Interlocked.Increment(ref dlSuccess);
                        var done = Interlocked.Increment(ref dlDone);
                        var succ = Volatile.Read(ref dlSuccess);
                        dlTask.Value = done;
                        dlTask.Description = $"[cyan]Загрузка конфигов[/]  [green]✓ {succ}[/] [red]✗ {done - succ}[/]  [grey]{done}/{urls.Length}[/]";
                    }));

                    dlTask.Value = dlTask.MaxValue;

                    foreach (var (url, content, ok) in downloadResults)
                    {
                        if (ok && content != null)
                            AddConfigContent(combinedLines, content, url);
                        else
                            failedDownloads.Add(url);
                    }

                    if (combinedLines.Count == 0)
                    {
                        if (File.Exists(_config.SourceConfigFile))
                        {
                            var content = await File.ReadAllTextAsync(_config.SourceConfigFile, cancellationToken);
                            AddConfigContent(combinedLines, content, "local");
                        }
                        else
                        {
                            _logger.LogError("Нет данных для тестирования.");
                            return;
                        }
                    }

                    try
                    {
                        await File.WriteAllLinesAsync(_config.SourceConfigFile,
                            combinedLines.Select(x => x.Line), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Не удалось сохранить кэш конфига: {ex.Message}");
                    }
                }

                foreach (var server in _config.CustomServers)
                    combinedLines.Add((server, "custom"));

                servers = _serverParser.ParseServers(
                    combinedLines.Where(x => !string.IsNullOrWhiteSpace(x.Line)).ToList());

                if (servers.Count == 0)
                {
                    _logger.LogError("Не найдено серверов для тестирования");
                    return;
                }

                // ── DNS Resolve ───────────────────────────────────────────
                var hostnames = servers
                    .Select(s => s.Host)
                    .Where(IsHostname)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (hostnames.Count > 0)
                {
                    var dnsTask = ctx.AddTask(
                        $"[cyan]DNS резолв[/]  [grey]0/{hostnames.Count}[/]",
                        maxValue: hostnames.Count);

                    var resolvedIps = await _dnsResolver.ResolveBatchAsync(
                        hostnames,
                        (done, total, resolved) =>
                        {
                            dnsTask.Value = done;
                            dnsTask.Description = $"[cyan]DNS резолв[/]  [green]✓ {resolved}[/] [red]✗ {done - resolved}[/]  [grey]{done}/{total}[/]";
                        },
                        cancellationToken);

                    dnsTask.Value = dnsTask.MaxValue;

                    serversWithResolvedIp = servers
                        .Select(s => resolvedIps.TryGetValue(s.Host, out var ip)
                            ? s with { ResolvedIpAddress = ip }
                            : s)
                        .ToList();
                }
                else
                {
                    serversWithResolvedIp = servers;
                }

                // ── TCP Test ──────────────────────────────────────────────
                var serversWithIp = serversWithResolvedIp
                    .Select(s => new { Server = s, Ip = s.GetIpAddressOrHost() })
                    .Where(x => System.Net.IPAddress.TryParse(x.Ip, out _))
                    .ToList();

                var uniqueEndpoints = serversWithIp
                    .Select(x => (IpAddress: x.Ip, x.Server.Port))
                    .Distinct()
                    .ToList();

                var uniqueConfigsByEndpoint = serversWithIp
                    .GroupBy(x => ServerTester.BuildEndpointKey(x.Ip, x.Server.Port), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => DeduplicateByConnectionParameters(g.Select(x => x.Server).ToList()),
                        StringComparer.OrdinalIgnoreCase);

                var tcpTask = ctx.AddTask(
                    $"[cyan]TCP тестирование[/]  [grey]0/{uniqueEndpoints.Count}[/]",
                    maxValue: Math.Max(1, uniqueEndpoints.Count));

                var successfulEndpoints = await _serverTester.TestUniqueEndpointsAsync(
                    uniqueEndpoints,
                    (tested, total, ok) =>
                    {
                        tcpTask.Value = tested;
                        tcpTask.Description = $"[cyan]TCP тестирование[/]  [green]✓ {ok}[/] [red]✗ {tested - ok}[/]  [grey]{tested}/{total}[/]";
                    },
                    cancellationToken);

                tcpTask.Value = tcpTask.MaxValue;

                successfulServers = successfulEndpoints
                    .Where(uniqueConfigsByEndpoint.ContainsKey)
                    .SelectMany(ep => uniqueConfigsByEndpoint[ep])
                    .ToList();

                // ── sing-box ──────────────────────────────────────────────
                finalSuccessfulServers = successfulServers;

                if (_config.SingBoxEnabled && !string.IsNullOrWhiteSpace(singBoxPath) && successfulServers.Count > 0)
                {
                    var sbTask = ctx.AddTask(
                        $"[cyan]sing-box тест[/]  [grey]0/{successfulServers.Count}[/]",
                        maxValue: Math.Max(1, successfulServers.Count));

                    var sbSuccessful = await _singBoxTester.TestAsync(
                        successfulServers,
                        singBoxPath,
                        (tested, total, ok) =>
                        {
                            sbTask.Value = tested;
                            sbTask.Description = $"[cyan]sing-box тест[/]  [green]✓ {ok}[/] [red]✗ {tested - ok}[/]  [grey]{tested}/{total}[/]";
                        },
                        cancellationToken);

                    sbTask.Value = sbTask.MaxValue;
                    finalSuccessfulServers = sbSuccessful;
                }
                else if (_config.SingBoxEnabled && string.IsNullOrWhiteSpace(singBoxPath))
                {
                    _logger.LogWarning("sing-box недоступен, дополнительная проверка пропущена.");
                }
            });

        foreach (var url in failedDownloads)
            _logger.LogError($"Не удалось скачать: {Shorten(url)}");

        if (finalSuccessfulServers.Count > 0 && servers.Count > 0)
        {
            var sourceStats = _configSourceAnalyzer.AnalyzeSources(serversWithResolvedIp, finalSuccessfulServers);
            _configSourceAnalyzer.PrintSourcesAnalysis(sourceStats);
            _configSourceAnalyzer.RecommendBestSources(sourceStats);
        }

        await SaveResultsAsync(finalSuccessfulServers, cancellationToken);
        if (finalSuccessfulServers.Count > 0)
            await AnalyzeIpRangesAsync(cancellationToken);
    }

    private static string Shorten(string s) => s.Length > 60 ? s[..57] + "..." : s;

    private async Task<(string Url, string? Content, bool Success)> DownloadOneAsync(
        string url, int index, CancellationToken ct)
    {
        var tempFile = $"{_config.SourceConfigFile}.download.{index}.tmp";
        var ok = await _configDownloader.DownloadAsync(url, tempFile, ct);
        if (!ok) return (url, null, false);
        try
        {
            var content = await File.ReadAllTextAsync(tempFile, ct);
            return (url, content, true);
        }
        catch { return (url, null, false); }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Запускает анализ существующих данных
    /// </summary>
    public async Task AnalyzeExistingDataAsync(string? inputFile = null)
    {
        var file = inputFile ?? _config.SuccessfulServersFile;
        if (!File.Exists(file))
        {
            AnsiConsole.MarkupLine($"[red]Файл {Markup.Escape(file)} не найден![/]");
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]=== Анализ IP-диапазонов: {Markup.Escape(file)} ===[/]\n");
        var ipRanges = _ipRangeAnalyzer.AnalyzeIpRanges(file);

        if (ipRanges.Count > 0)
        {
            await _ipRangeAnalyzer.EnrichProviderInfoAsync(ipRanges);
            _ipRangeAnalyzer.PrintAnalysis(ipRanges);
            _ipRangeAnalyzer.SaveRangesToFile(ipRanges, _config.IpRangesFile);
            AnsiConsole.MarkupLine($"\n[green]Диапазоны IP сохранены в:[/] {Markup.Escape(_config.IpRangesFile)}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Не найдено IP-адресов для анализа.[/]\n[grey]Файл должен содержать VPN URI с IP-адресами (не доменными именами).[/]");
        }
    }

    private void AddConfigContent(List<(string Line, string SourceUrl)> combinedLines, string content, string sourceUrl)
    {
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
        if (VpnProtocols.AllSchemes.Any(s => trimmed.Contains(s, StringComparison.OrdinalIgnoreCase)))
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
        var normalized = Base64Helper.PrepareForDecode(content);
        if (normalized.Length < 32 || !Base64Helper.IsLikelyEncoded(normalized))
            return false;

        var result = Base64Helper.Decode(normalized);
        if (string.IsNullOrWhiteSpace(result))
            return false;

        decoded = result.Trim();
        var d = decoded;
        return VpnProtocols.AllSchemes.Any(s => d.Contains(s, StringComparison.OrdinalIgnoreCase));
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

    private static string NormalizeUrlForComparison(string url)
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

        _logger.LogResult($"Результаты сохранены:");
        _logger.LogResult($"  - Список серверов: {_config.SuccessfulServersFile}");
        _logger.LogResult($"  - Конфиг: {_config.OutputConfigFile}");
    }

    private async Task AnalyzeIpRangesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("");
        _logger.LogInfo("Анализ IP-диапазонов...");

        var ipRanges = _ipRangeAnalyzer.AnalyzeIpRanges(_config.SuccessfulServersFile);

        if (ipRanges.Count > 0)
        {
            await _ipRangeAnalyzer.EnrichProviderInfoAsync(ipRanges, cancellationToken);
            _ipRangeAnalyzer.PrintAnalysis(ipRanges);
            _ipRangeAnalyzer.SaveRangesToFile(ipRanges, _config.IpRangesFile);
            _logger.LogResult($"\nДиапазоны IP сохранены в: {_config.IpRangesFile}");
        }
    }
}
