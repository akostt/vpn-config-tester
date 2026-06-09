using Spectre.Console;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnCheck;
using VpnCheck.Infrastructure;
using VpnCheck.Localization;
using VpnCheck.Models;
using VpnCheck.Services;

const string SettingsFile = "settings.json";
const string SourcesFile = "sources.txt";

SettingsManager.EnsureDefaultFile(SettingsFile);
SourcesManager.EnsureDefaultFile(SourcesFile);

// Headless mode
if (args.Length > 0)
{
    var settings = SettingsManager.Load(SettingsFile);
    var sources = SourcesManager.Load(SourcesFile);
    switch (args[0])
    {
        case "--run":
            await RunApp(false, settings, sources);
            return;
        case "--local": case "-l":
            await RunApp(true, settings, sources);
            return;
        case "--analyze": case "-a":
            BuildApp(settings, sources).AnalyzeExistingData();
            return;
    }
}

// Interactive menu
while (true)
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new Rule("[bold cyan]VPNCheck[/]").RuleStyle("grey").LeftJustified());
    AnsiConsole.WriteLine();

    var settings = SettingsManager.Load(SettingsFile);
    var sources = SourcesManager.Load(SourcesFile);

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{Loc.MenuTitle}[/]")
            .HighlightStyle("cyan")
            .AddChoices(
                Loc.MenuRun,
                Loc.MenuLocal,
                Loc.MenuSources,
                Loc.MenuSettings,
                Loc.MenuAnalyze,
                Loc.MenuTools,
                Loc.MenuExit
            ));

    if (choice == Loc.MenuRun)
        await RunApp(false, settings, sources);
    else if (choice == Loc.MenuLocal)
        await RunApp(true, settings, sources);
    else if (choice == Loc.MenuAnalyze)
    {
        BuildApp(settings, sources).AnalyzeExistingData();
        Pause();
    }
    else if (choice == Loc.MenuSources)
        ManageSources(SourcesFile);
    else if (choice == Loc.MenuSettings)
        ManageSettings(SettingsFile, settings);
    else if (choice == Loc.MenuTools)
        await NetworkTools();
    else
        break;
}

async Task RunApp(bool skipDownload, AppSettings s, SourcesList src)
{
    try
    {
        await BuildApp(s, src).RunAsync(skipDownload);
        Pause();
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]{Loc.CriticalError}[/] {Markup.Escape(ex.Message)}");
        Pause();
    }
}

Application BuildApp(AppSettings s, SourcesList src)
{
    var config = new ApplicationConfiguration
    {
        SubscriptionUrls = src.Subscriptions.ToArray(),
        CustomServers = src.CustomServers.ToArray(),
        SingBoxEnabled = s.SingBoxEnabled,
        TcpTimeoutMs = s.TcpTimeoutMs,
        MaxConcurrentTests = s.MaxConcurrentTests,
        HttpTimeoutSeconds = s.HttpTimeoutSeconds,
        SingBoxToolsDirectory = s.SingBoxToolsDir,
        SingBoxTestTimeoutSeconds = s.SingBoxTimeoutSeconds,
        MaxConcurrentSingBoxTests = s.MaxConcurrentSingBoxTests,
        MinIpCountForSubnet24 = s.MinIpCountForSubnet24,
        MinIpCountForSubnet16 = s.MinIpCountForSubnet16,
    };
    var logger = new ConsoleLogger(s.LogLevel);
    var configDownloader = new ConfigDownloader(config, logger);
    var serverParser = new ServerParser(logger);
    var serverTester = new ServerTester(config, logger);
    var dnsResolver = new DnsResolverService(logger);
    var configWriter = new ConfigWriter(logger);
    var ipRangeAnalyzer = new IpRangeAnalyzerService(config, logger);
    var configSourceAnalyzer = new ConfigSourceAnalyzer(logger);
    var singBoxManager = new SingBoxManager(config, logger);
    var singBoxConfigBuilder = new SingBoxConfigBuilder(logger);
    var singBoxTester = new SingBoxTester(config, singBoxConfigBuilder, logger);
    return new Application(config, configDownloader, serverParser, serverTester, configWriter,
        ipRangeAnalyzer, configSourceAnalyzer, singBoxManager, singBoxTester, dnsResolver, logger);
}

void ManageSources(string filePath)
{
    while (true)
    {
        var src = SourcesManager.Load(filePath);
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold cyan]{Loc.SourcesTitle}[/]").LeftJustified());

        // Build flat list: subscriptions first, then custom servers
        var allItems = src.Subscriptions
            .Select(u => (Url: u, Type: Loc.TypeSubscription, IsServer: false))
            .Concat(src.CustomServers
            .Select(u => (Url: u, Type: Loc.TypeServer, IsServer: true)))
            .ToList();

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn($"[grey]{Loc.ColNumber}[/]")
             .AddColumn(Loc.ColSource)
             .AddColumn($"[grey]{Loc.ColType}[/]");
        for (int i = 0; i < allItems.Count; i++)
        {
            var (url, type, isServer) = allItems[i];
            var typeMarkup = isServer ? $"[green]{type}[/]" : $"[blue]{type}[/]";
            table.AddRow($"[grey]{i + 1}[/]", Markup.Escape(Shorten(url)), typeMarkup);
        }
        AnsiConsole.Write(table);

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(":")
                .HighlightStyle("cyan")
                .AddChoices(
                    Loc.ActionAdd,
                    Loc.ActionAddServer,
                    Loc.ActionRemove,
                    Loc.ActionBack));

        if (action == Loc.ActionAdd)
        {
            var url = AnsiConsole.Ask<string>(Loc.AskUrl);
            if (string.IsNullOrWhiteSpace(url))
                AnsiConsole.MarkupLine($"[grey]{Loc.Cancelled}[/]");
            else
            {
                src.Subscriptions.Add(url.Trim());
                SourcesManager.Save(filePath, src);
                AnsiConsole.MarkupLine($"[green]{Loc.Added}[/]");
            }
        }
        else if (action == Loc.ActionAddServer)
        {
            var uri = AnsiConsole.Ask<string>(Loc.AskUri);
            if (string.IsNullOrWhiteSpace(uri))
                AnsiConsole.MarkupLine($"[grey]{Loc.Cancelled}[/]");
            else if (SourcesManager.IsVpnUri(uri.Trim()))
            {
                src.CustomServers.Add(uri.Trim());
                SourcesManager.Save(filePath, src);
                AnsiConsole.MarkupLine($"[green]{Loc.Added}[/]");
            }
            else
                AnsiConsole.MarkupLine($"[red]{Loc.InvalidUri}[/]");
        }
        else if (action == Loc.ActionRemove)
        {
            var num = AnsiConsole.Ask<string>(Loc.AskRemoveNum);
            if (string.IsNullOrWhiteSpace(num))
                AnsiConsole.MarkupLine($"[grey]{Loc.Cancelled}[/]");
            else
                TryRemoveSource(filePath, src, num.Trim());
        }
        else break;
    }
}

void TryRemoveSource(string filePath, SourcesList src, string num)
{
    if (!int.TryParse(num, out var idx) || idx < 1)
    {
        AnsiConsole.MarkupLine($"[red]{Loc.NotFound}[/]");
        return;
    }

    var subCount = src.Subscriptions.Count;
    if (idx <= subCount)
    {
        src.Subscriptions.RemoveAt(idx - 1);
        SourcesManager.Save(filePath, src);
        AnsiConsole.MarkupLine($"[green]{Loc.Removed}[/]");
    }
    else if (idx <= subCount + src.CustomServers.Count)
    {
        src.CustomServers.RemoveAt(idx - subCount - 1);
        SourcesManager.Save(filePath, src);
        AnsiConsole.MarkupLine($"[green]{Loc.Removed}[/]");
    }
    else
        AnsiConsole.MarkupLine($"[red]{Loc.NotFound}[/]");
}

void ManageSettings(string filePath, AppSettings s)
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold cyan]{Loc.SettingsTitle}[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(Loc.ColParam).AddColumn(Loc.ColValue);
        table.AddRow(Loc.SettingTcpTimeout, s.TcpTimeoutMs.ToString());
        table.AddRow(Loc.SettingConcurrent, s.MaxConcurrentTests.ToString());
        table.AddRow(Loc.SettingHttpTimeout, s.HttpTimeoutSeconds.ToString());
        table.AddRow("sing-box", s.SingBoxEnabled
            ? $"[green]{Loc.SingBoxOn}[/]"
            : $"[grey]{Loc.SingBoxOff}[/]");
        table.AddRow(Loc.SettingSingBoxTimeout, s.SingBoxTimeoutSeconds.ToString());
        table.AddRow(Loc.SettingSingBoxConcurrent, s.MaxConcurrentSingBoxTests.ToString());
        table.AddRow(Loc.SettingLogLevel, $"[cyan]{LogLevelDisplay(s.LogLevel)}[/]");
        AnsiConsole.Write(table);

        var field = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Loc.ActionChange}[/]")
                .HighlightStyle("cyan")
                .AddChoices(
                    Loc.SettingTcpTimeout,
                    Loc.SettingConcurrent,
                    Loc.SettingHttpTimeout,
                    Loc.SettingSingBox,
                    Loc.SettingSingBoxTimeout,
                    Loc.SettingSingBoxConcurrent,
                    Loc.SettingLogLevel,
                    Loc.ActionBack));

        if (field == Loc.SettingTcpTimeout)
            s.TcpTimeoutMs = AnsiConsole.Ask(Loc.AskValue, s.TcpTimeoutMs);
        else if (field == Loc.SettingConcurrent)
            s.MaxConcurrentTests = AnsiConsole.Ask(Loc.AskValue, s.MaxConcurrentTests);
        else if (field == Loc.SettingHttpTimeout)
            s.HttpTimeoutSeconds = AnsiConsole.Ask(Loc.AskValue, s.HttpTimeoutSeconds);
        else if (field == Loc.SettingSingBox)
            s.SingBoxEnabled = !s.SingBoxEnabled;
        else if (field == Loc.SettingSingBoxTimeout)
            s.SingBoxTimeoutSeconds = AnsiConsole.Ask(Loc.AskValue, s.SingBoxTimeoutSeconds);
        else if (field == Loc.SettingSingBoxConcurrent)
            s.MaxConcurrentSingBoxTests = AnsiConsole.Ask(Loc.AskValue, s.MaxConcurrentSingBoxTests);
        else if (field == Loc.SettingLogLevel)
        {
            var levels = new[] {
                ("error",   Loc.LogLevelError),
                ("warning", Loc.LogLevelWarning),
                ("all",     Loc.LogLevelAll),
                ("none",    Loc.LogLevelNone),
            };
            var currentDisplay = levels.FirstOrDefault(l => l.Item1 == s.LogLevel).Item2 ?? levels[0].Item2;
            var ordered = new[] { currentDisplay }
                .Concat(levels.Select(l => l.Item2).Where(d => d != currentDisplay))
                .ToArray();
            var chosen = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold]{Loc.SettingLogLevel}[/]")
                    .HighlightStyle("cyan")
                    .AddChoices(ordered));
            s.LogLevel = levels.First(l => l.Item2 == chosen).Item1;
        }
        else
        {
            SettingsManager.Save(filePath, s);
            return;
        }

        SettingsManager.Save(filePath, s);
        AnsiConsole.MarkupLine($"[green]{Loc.Saved}[/]");
    }
}

void Pause()
{
    AnsiConsole.MarkupLine($"\n[grey]{Loc.PressAnyKey}[/]");
    try { Console.ReadKey(true); } catch { }
}

string Shorten(string s) => s.Length > 80 ? s[..77] + "..." : s;

string LogLevelDisplay(string level) => level switch
{
    "error"   => Loc.LogLevelError,
    "warning" => Loc.LogLevelWarning,
    "all"     => Loc.LogLevelAll,
    "none"    => Loc.LogLevelNone,
    _         => level
};

void ToolHeader(string title, string description)
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new Rule($"[bold cyan]{title}[/]").LeftJustified());
    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(description)}[/]");
    AnsiConsole.WriteLine();
}

async Task NetworkTools()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold cyan]{Loc.ToolsTitle}[/]").LeftJustified());

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Loc.MenuTitle}[/]")
                .HighlightStyle("cyan")
                .AddChoices(
                    Loc.ToolMyIp,
                    Loc.ToolIpInfo,
                    Loc.ToolDnsLookup,
                    Loc.ToolReverseDns,
                    Loc.ToolPing,
                    Loc.ToolPortCheck,
                    Loc.ActionBack));

        if (choice == Loc.ToolMyIp)          await ToolMyIpAsync();
        else if (choice == Loc.ToolIpInfo)   await ToolIpInfoAsync();
        else if (choice == Loc.ToolDnsLookup)    await ToolDnsLookupAsync();
        else if (choice == Loc.ToolReverseDns)   await ToolReverseDnsAsync();
        else if (choice == Loc.ToolPing)         await ToolPingAsync();
        else if (choice == Loc.ToolPortCheck)    await ToolPortCheckAsync();
        else break;
    }
}

async Task ToolDnsLookupAsync()
{
    ToolHeader(Loc.ToolDnsLookup, "Переводит доменное имя в IP-адрес (A и AAAA записи).");
    var host = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Домен [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    await AnsiConsole.Status().StartAsync("Резолв...", async _ =>
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            AnsiConsole.WriteLine();
            var t = new Table().Border(TableBorder.Rounded).Expand()
                .AddColumn("[grey]Тип[/]").AddColumn("IP-адрес");
            foreach (var a in addrs)
                t.AddRow(a.AddressFamily == AddressFamily.InterNetwork ? "[blue]A[/]" : "[cyan]AAAA[/]",
                         a.ToString());
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(host)}[/]");
            AnsiConsole.Write(t);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Ошибка:[/] {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}

async Task ToolMyIpAsync()
{
    ToolHeader(Loc.ToolMyIp, "Определяет ваш внешний IP-адрес (IPv4 и IPv6) через внешние сервисы.");
    await AnsiConsole.Status().StartAsync("Получение IP...", async _ =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var t = new Table().Border(TableBorder.Rounded)
            .AddColumn("[grey]Версия[/]").AddColumn("Адрес");
        try
        {
            var v4 = await http.GetStringAsync("https://api.ipify.org");
            t.AddRow("[blue]IPv4[/]", Markup.Escape(v4.Trim()));
        }
        catch { t.AddRow("[blue]IPv4[/]", "[grey]недоступен[/]"); }
        try
        {
            var v6 = await http.GetStringAsync("https://api6.ipify.org");
            t.AddRow("[cyan]IPv6[/]", Markup.Escape(v6.Trim()));
        }
        catch { t.AddRow("[cyan]IPv6[/]", "[grey]недоступен[/]"); }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Мой IP[/]").LeftJustified());
        AnsiConsole.Write(t);
    });
    Pause();
}

async Task ToolIpInfoAsync()
{
    ToolHeader(Loc.ToolIpInfo, "Информация об IP-адресе или домене: страна, провайдер, AS, наличие прокси/VPN.");
    var host = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]IP или домен [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    await AnsiConsole.Status().StartAsync("Запрос информации...", async _ =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync(
                $"http://ip-api.com/json/{Uri.EscapeDataString(host)}?fields=status,message,country,countryCode,regionName,city,isp,org,as,asname,reverse,proxy,hosting,query");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            if (r.TryGetProperty("status", out var st) && st.GetString() != "success")
            {
                var msg = r.TryGetProperty("message", out var m) ? m.GetString() : "неизвестная ошибка";
                AnsiConsole.MarkupLine($"\n[red]Ошибка:[/] {Markup.Escape(msg ?? "")}");
                return;
            }

            string G(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            bool B(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

            var t = new Table().Border(TableBorder.Rounded).Expand()
                .AddColumn("[grey]Параметр[/]").AddColumn("Значение");
            void Row(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) t.AddRow($"[grey]{k}[/]", Markup.Escape(v)); }

            Row("IP-адрес",   G("query"));
            Row("Страна",     $"{G("country")} ({G("countryCode")})");
            Row("Регион",     G("regionName"));
            Row("Город",      G("city"));
            Row("ISP",        G("isp"));
            Row("Организация",G("org"));
            Row("AS",         G("as"));
            Row("AS имя",     G("asname"));
            Row("rDNS",       G("reverse"));
            t.AddRow("[grey]Прокси/VPN[/]", B("proxy") ? "[yellow]да[/]" : "[green]нет[/]");
            t.AddRow("[grey]Хостинг[/]",    B("hosting") ? "[yellow]да[/]" : "[green]нет[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]Информация: {Markup.Escape(G("query"))}[/]").LeftJustified());
            AnsiConsole.Write(t);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Ошибка:[/] {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}

async Task ToolReverseDnsAsync()
{
    ToolHeader(Loc.ToolReverseDns, "Обратный DNS — находит доменное имя по IP-адресу (PTR запись).");
    var ip = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]IP-адрес [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(ip)) return;

    await AnsiConsole.Status().StartAsync("PTR запрос...", async _ =>
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]IP:[/] {Markup.Escape(ip)}");
            AnsiConsole.MarkupLine($"[grey]Хост:[/] [bold]{Markup.Escape(entry.HostName)}[/]");
            if (entry.Aliases.Length > 0)
            {
                AnsiConsole.MarkupLine("[grey]Псевдонимы:[/]");
                foreach (var a in entry.Aliases)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(a)}");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Ошибка:[/] {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}

async Task ToolPingAsync()
{
    ToolHeader(Loc.ToolPing, "ICMP-пинг — измеряет задержку и потери пакетов (4 пакета).");
    var host = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Хост или IP [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    const int count = 4;
    var results = new List<(bool ok, long ms)>();

    await AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"[cyan]Пинг {Markup.Escape(host)}[/]", maxValue: count);
            using var ping = new System.Net.NetworkInformation.Ping();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 3000);
                    var ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                    results.Add((ok, ok ? reply.RoundtripTime : -1));
                    task.Description = ok
                        ? $"[cyan]Пинг {Markup.Escape(host)}[/]  [green]{reply.RoundtripTime} мс[/]"
                        : $"[cyan]Пинг {Markup.Escape(host)}[/]  [red]таймаут[/]";
                }
                catch (Exception ex)
                {
                    results.Add((false, -1));
                    task.Description = $"[cyan]Пинг {Markup.Escape(host)}[/]  [red]{Markup.Escape(ex.Message)}[/]";
                }
                task.Increment(1);
                if (i < count - 1) await Task.Delay(500);
            }
        });

    var ok2 = results.Where(r => r.ok).ToList();
    var loss = results.Count - ok2.Count;
    AnsiConsole.WriteLine();
    var t = new Table().Border(TableBorder.Rounded)
        .AddColumn("[grey]Параметр[/]").AddColumn("Значение");
    t.AddRow("[grey]Отправлено[/]",  count.ToString());
    t.AddRow("[grey]Потеряно[/]",    loss == 0 ? "[green]0[/]" : $"[red]{loss}[/]");
    if (ok2.Count > 0)
    {
        t.AddRow("[grey]Мин[/]",  $"[cyan]{ok2.Min(r => r.ms)} мс[/]");
        t.AddRow("[grey]Макс[/]", $"[cyan]{ok2.Max(r => r.ms)} мс[/]");
        t.AddRow("[grey]Среднее[/]", $"[cyan]{ok2.Average(r => r.ms):F0} мс[/]");
    }
    AnsiConsole.Write(t);
    Pause();
}

async Task ToolPortCheckAsync()
{
    ToolHeader(Loc.ToolPortCheck, "Проверяет доступность TCP-порта на удалённом хосте.");
    var hostInput = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Хост или хост:порт [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(hostInput)) return;

    string host;
    int port;
    if (hostInput.Contains(':') && int.TryParse(hostInput.Split(':')[^1], out var p))
    {
        port = p;
        host = string.Join(':', hostInput.Split(':')[..^1]);
    }
    else
    {
        host = hostInput;
        var portStr = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Порт [grey](Enter — отмена)[/]:[/]").AllowEmpty()).Trim();
        if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out port)) return;
    }

    await AnsiConsole.Status().StartAsync($"Проверка {host}:{port}...", async _ =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            var ct = new CancellationTokenSource(5000);
            await tcp.ConnectAsync(host, port, ct.Token);
            sw.Stop();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓ Открыт[/]  [grey]{host}:{port}[/]  [cyan]{sw.ElapsedMilliseconds} мс[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"\n[red]✗ Таймаут[/]  [grey]{host}:{port}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]✗ Закрыт[/]  [grey]{host}:{port}[/]  {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}
