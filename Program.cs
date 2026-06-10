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
Loc.Reload(SettingsManager.Load(SettingsFile).Language);

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
            await BuildApp(settings, sources).AnalyzeExistingDataAsync();
            return;
    }
}

// Interactive menu
while (true)
{
    ShowBanner();

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
                Loc.MenuExport,
                Loc.MenuTools,
                Loc.MenuExit
            ));

    if (choice == Loc.MenuRun)
        await RunApp(false, settings, sources);
    else if (choice == Loc.MenuLocal)
        await RunApp(true, settings, sources);
    else if (choice == Loc.MenuAnalyze)
    {
        ShowSubHeader(Loc.MenuAnalyze);

        var fileChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Loc.AnalyzeSelectFile}[/]")
                .HighlightStyle("cyan")
                .AddChoices(
                    Loc.AnalyzeFileOutput,
                    Loc.AnalyzeFileServers,
                    Loc.AnalyzeFileCustom,
                    Loc.ActionBack));

        if (fileChoice != Loc.ActionBack)
        {
            string? filePath = null;
            if (fileChoice == Loc.AnalyzeFileOutput)
                filePath = "output_config.txt";
            else if (fileChoice == Loc.AnalyzeFileServers)
                filePath = "successful_servers.txt";
            else
            {
                var custom = AnsiConsole.Ask<string>(Loc.AnalyzeAskPath);
                if (!string.IsNullOrWhiteSpace(custom)) filePath = custom.Trim();
            }

            if (filePath != null)
            {
                await BuildApp(settings, sources).AnalyzeExistingDataAsync(filePath);
                Pause();
            }
        }
    }
    else if (choice == Loc.MenuSources)
        ManageSources(SourcesFile);
    else if (choice == Loc.MenuSettings)
        ManageSettings(SettingsFile, settings);
    else if (choice == Loc.MenuExport)
        await ExportResults();
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
    var downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(s.HttpTimeoutSeconds) };
    var singBoxHttpClient  = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var configDownloader = new ConfigDownloader(downloadHttpClient, config, logger);
    var serverParser = new ServerParser(logger);
    var serverTester = new ServerTester(config, logger);
    var dnsResolver = new DnsResolverService(logger);
    var configWriter = new ConfigWriter(logger);
    var ipRangeAnalyzer = new IpRangeAnalyzerService(config, logger);
    var configSourceAnalyzer = new ConfigSourceAnalyzer(logger);
    var singBoxManager = new SingBoxManager(singBoxHttpClient, config, logger);
    var singBoxConfigBuilder = new SingBoxConfigBuilder(logger);
    var singBoxTester = new SingBoxTester(config, singBoxConfigBuilder, logger);
    return new Application(config, configDownloader, serverParser, serverTester, configWriter,
        ipRangeAnalyzer, configSourceAnalyzer, singBoxManager, singBoxTester, dnsResolver, logger);
}

void ManageSources(string filePath)
{
    var original = SourcesManager.Load(filePath);
    var src = new SourcesList();
    foreach (var u in original.Subscriptions) src.Subscriptions.Add(u);
    foreach (var u in original.CustomServers)  src.CustomServers.Add(u);

    var added   = new List<(string Url, bool IsServer)>();
    var removed = new List<(string Url, bool IsServer)>();

    while (true)
    {
        ShowSubHeader(Loc.SourcesTitle);

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
                .Title($"[bold]{Loc.ActionChange}[/]")
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
                var trimmed = url.Trim();
                src.Subscriptions.Add(trimmed);
                added.Add((trimmed, false));
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
                var trimmed = uri.Trim();
                src.CustomServers.Add(trimmed);
                added.Add((trimmed, true));
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
                TryRemoveSource(src, added, removed, num.Trim());
        }
        else
        {
            if (added.Count > 0 || removed.Count > 0)
            {
                ShowSubHeader(Loc.SourcesChangedTitle);

                var diffTable = new Table().Border(TableBorder.Rounded).Expand();
                diffTable.AddColumn($"[grey]{Loc.ColChange}[/]")
                         .AddColumn(Loc.ColSource)
                         .AddColumn($"[grey]{Loc.ColType}[/]");

                foreach (var (url, isServer) in added)
                {
                    var typeLabel = isServer ? $"[green]{Loc.TypeServer}[/]" : $"[blue]{Loc.TypeSubscription}[/]";
                    diffTable.AddRow("[green]+[/]", Markup.Escape(Shorten(url)), typeLabel);
                }
                foreach (var (url, isServer) in removed)
                {
                    var typeLabel = isServer ? $"[green]{Loc.TypeServer}[/]" : $"[blue]{Loc.TypeSubscription}[/]";
                    diffTable.AddRow("[red]−[/]", Markup.Escape(Shorten(url)), typeLabel);
                }
                AnsiConsole.Write(diffTable);

                var saveAction = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[bold]{Loc.SaveChangesPrompt}[/]")
                        .HighlightStyle("cyan")
                        .AddChoices(Loc.ActionSave, Loc.ActionDiscard));

                if (saveAction == Loc.ActionSave)
                {
                    SourcesManager.Save(filePath, src);
                    AnsiConsole.MarkupLine($"[green]{Loc.Saved}[/]");
                    Pause();
                }
            }
            break;
        }
    }
}

void TryRemoveSource(SourcesList src, List<(string Url, bool IsServer)> added, List<(string Url, bool IsServer)> removed, string num)
{
    if (!int.TryParse(num, out var idx) || idx < 1)
    {
        AnsiConsole.MarkupLine($"[red]{Loc.NotFound}[/]");
        return;
    }

    var subCount = src.Subscriptions.Count;
    if (idx <= subCount)
    {
        var url = src.Subscriptions[idx - 1];
        src.Subscriptions.RemoveAt(idx - 1);
        added.RemoveAll(x => x.Url == url && !x.IsServer);
        if (!removed.Any(x => x.Url == url && !x.IsServer))
            removed.Add((url, false));
        AnsiConsole.MarkupLine($"[green]{Loc.Removed}[/]");
    }
    else if (idx <= subCount + src.CustomServers.Count)
    {
        var url = src.CustomServers[idx - subCount - 1];
        src.CustomServers.RemoveAt(idx - subCount - 1);
        added.RemoveAll(x => x.Url == url && x.IsServer);
        if (!removed.Any(x => x.Url == url && x.IsServer))
            removed.Add((url, true));
        AnsiConsole.MarkupLine($"[green]{Loc.Removed}[/]");
    }
    else
        AnsiConsole.MarkupLine($"[red]{Loc.NotFound}[/]");
}

void ManageSettings(string filePath, AppSettings s)
{
    while (true)
    {
        ShowSubHeader(Loc.SettingsTitle);

        var table = new Table().Border(TableBorder.Rounded).Expand();
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
        table.AddRow(Loc.SettingLanguage, $"[cyan]{LanguageDisplay(s.Language)}[/]");
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
                    Loc.SettingLanguage,
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
        else if (field == Loc.SettingLanguage)
        {
            var langs = new[] { ("auto", Loc.LangAuto), ("ru", Loc.LangRu), ("en", Loc.LangEn) };
            var currentDisplay = langs.FirstOrDefault(l => l.Item1 == s.Language).Item2 ?? langs[0].Item2;
            var ordered = new[] { currentDisplay }
                .Concat(langs.Select(l => l.Item2).Where(d => d != currentDisplay))
                .ToArray();
            var chosen2 = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold]{Loc.SettingLanguage}[/]")
                    .HighlightStyle("cyan")
                    .AddChoices(ordered));
            s.Language = langs.First(l => l.Item2 == chosen2).Item1;
            SettingsManager.Save(filePath, s);
            Loc.Reload(s.Language);
            AnsiConsole.MarkupLine($"[green]{Loc.Saved}[/]");
            continue;
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

string LanguageDisplay(string lang) => lang switch
{
    "ru" => Loc.LangRu,
    "en" => Loc.LangEn,
    _    => Loc.LangAuto
};

async Task ExportResults()
{
    ShowSubHeader(Loc.ExportTitle);

    const string sourceFile = "output_config.txt";
    if (!File.Exists(sourceFile) || !File.ReadLines(sourceFile).Any(l => !string.IsNullOrWhiteSpace(l)))
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(Loc.ExportNoData)}[/]");
        Pause();
        return;
    }

    var lines = (await File.ReadAllLinesAsync(sourceFile))
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .ToList();

    var format = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{Loc.ExportFormatTitle}[/]")
            .HighlightStyle("cyan")
            .AddChoices(
                Loc.ExportFormatPlain,
                Loc.ExportFormatBase64,
                Loc.ExportFormatSingBox,
                Loc.ActionBack));

    if (format == Loc.ActionBack) return;

    string defaultPath;
    string content;

    if (format == Loc.ExportFormatBase64)
    {
        defaultPath = "export.b64";
        content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)));
    }
    else if (format == Loc.ExportFormatSingBox)
    {
        defaultPath = "export_singbox.json";
        var outbounds = new System.Text.Json.Nodes.JsonArray();
        var builder = new SingBoxConfigBuilder();
        int tag = 1;
        foreach (var line in lines)
        {
            var dummy = new ServerInfo { OriginalUrl = line };
            if (builder.TryBuildOutbound(dummy, $"proxy-{tag}", out var ob))
            {
                outbounds.Add(ob);
                tag++;
            }
        }
        var root = new System.Text.Json.Nodes.JsonObject { ["outbounds"] = outbounds };
        content = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    else
    {
        defaultPath = "export_uris.txt";
        content = string.Join('\n', lines);
    }

    var pathInput = AnsiConsole.Prompt(
        new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.ExportPathPrompt)}[/] [grey]({Markup.Escape(defaultPath)})[/]")
            .AllowEmpty()).Trim();

    var outputPath = string.IsNullOrWhiteSpace(pathInput) ? defaultPath : pathInput;

    try
    {
        await File.WriteAllTextAsync(outputPath, content, System.Text.Encoding.UTF8);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(Loc.ExportDone)}[/] [bold]{Markup.Escape(outputPath)}[/]  [grey]({lines.Count} {Markup.Escape(Loc.UnitServers)})[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(Loc.ExportError)}[/] {Markup.Escape(ex.Message)}");
    }

    Pause();
}

void ShowBanner()
{
    AnsiConsole.Clear();
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new FigletText("VPNCheck").Centered().Color(Color.Cyan1));
    AnsiConsole.Write(new Rule($"[dim]{Markup.Escape(Loc.ToolsSubtitle)}[/]").Centered().RuleStyle("grey dim"));
    AnsiConsole.WriteLine();
}

void ShowSubHeader(string section, string? description = null)
{
    AnsiConsole.Clear();
    AnsiConsole.WriteLine();
    AnsiConsole.Write(
        new Rule($"[grey]VPNCheck[/] [dim]·[/] [bold cyan]{Markup.Escape(section)}[/]")
            .LeftJustified().RuleStyle("grey dim"));
    if (description != null)
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(description)}[/]");
    AnsiConsole.WriteLine();
}

void ToolHeader(string title, string description)
{
    ShowSubHeader(title, description);
}

async Task NetworkTools()
{
    while (true)
    {
        ShowSubHeader(Loc.ToolsTitle);

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
    ToolHeader(Loc.ToolDnsLookup, Loc.ToolDnsLookupDesc);
    var host = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskDomain)}[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    await AnsiConsole.Status().StartAsync(Loc.StatusResolving, async _ =>
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            AnsiConsole.WriteLine();
            var t = new Table().Border(TableBorder.Rounded).Expand()
                .AddColumn($"[grey]{Markup.Escape(Loc.ColType)}[/]").AddColumn(Loc.ColIpAddress);
            foreach (var a in addrs)
                t.AddRow(a.AddressFamily == AddressFamily.InterNetwork ? "[blue]A[/]" : "[cyan]AAAA[/]",
                         a.ToString());
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(host)}[/]");
            AnsiConsole.Write(t);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(Loc.ErrorPrefix)}[/] {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}

async Task ToolMyIpAsync()
{
    ToolHeader(Loc.ToolMyIp, Loc.ToolMyIpDesc);
    await AnsiConsole.Status().StartAsync(Loc.StatusGettingIp, async _ =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var t = new Table().Border(TableBorder.Rounded)
            .AddColumn($"[grey]{Markup.Escape(Loc.ColVersion)}[/]").AddColumn(Loc.ColAddress);
        try
        {
            var v4 = await http.GetStringAsync("https://api.ipify.org");
            t.AddRow("[blue]IPv4[/]", Markup.Escape(v4.Trim()));
        }
        catch { t.AddRow("[blue]IPv4[/]", $"[grey]{Markup.Escape(Loc.StatusUnavailable)}[/]"); }
        try
        {
            var v6 = await http.GetStringAsync("https://api6.ipify.org");
            t.AddRow("[cyan]IPv6[/]", Markup.Escape(v6.Trim()));
        }
        catch { t.AddRow("[cyan]IPv6[/]", $"[grey]{Markup.Escape(Loc.StatusUnavailable)}[/]"); }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Loc.MyIpTitle)}[/]").LeftJustified());
        AnsiConsole.Write(t);
    });
    Pause();
}

async Task ToolIpInfoAsync()
{
    ToolHeader(Loc.ToolIpInfo, Loc.ToolIpInfoDesc);
    var host = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskIpOrDomain)}[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    await AnsiConsole.Status().StartAsync(Loc.StatusQuerying, async _ =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        Table BuildTable() => new Table().Border(TableBorder.Rounded).Expand()
            .AddColumn($"[grey]{Markup.Escape(Loc.ColParam)}[/]").AddColumn(Loc.ColValue);
        void Row(Table t, string k, string v) { if (!string.IsNullOrWhiteSpace(v)) t.AddRow($"[grey]{k}[/]", Markup.Escape(v)); }
        void Present(Table t, string ip)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Loc.IpInfoTitle)} {Markup.Escape(ip)}[/]").LeftJustified());
            AnsiConsole.Write(t);
        }

        Exception? lastError = null;

        // ── ip-api.com — полные данные, proxy/VPN, 45 req/мин ────────────
        try
        {
            var json = await http.GetStringAsync(
                $"https://ip-api.com/json/{Uri.EscapeDataString(host)}" +
                "?fields=status,message,country,countryCode,regionName,city,isp,org,as,asname,reverse,proxy,hosting,query");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("status", out var st) || st.GetString() != "success")
                throw new InvalidDataException(r.TryGetProperty("message", out var m) ? m.GetString() : null);

            string G(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            bool B(string k)   => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

            var t = BuildTable();
            Row(t, Loc.ColIpAddress,  G("query"));
            Row(t, Loc.LabelCountry,  $"{G("country")} ({G("countryCode")})");
            Row(t, Loc.LabelRegion,   G("regionName"));
            Row(t, Loc.LabelCity,     G("city"));
            Row(t, "ISP",             G("isp"));
            Row(t, Loc.LabelOrg,      G("org"));
            Row(t, "AS",              G("as"));
            Row(t, Loc.LabelAsName,   G("asname"));
            Row(t, "rDNS",            G("reverse"));
            t.AddRow($"[grey]{Markup.Escape(Loc.LabelProxyVpn)}[/]", B("proxy")   ? $"[yellow]{Markup.Escape(Loc.LabelYes)}[/]" : $"[green]{Markup.Escape(Loc.LabelNo)}[/]");
            t.AddRow($"[grey]{Markup.Escape(Loc.LabelHosting)}[/]",  B("hosting") ? $"[yellow]{Markup.Escape(Loc.LabelYes)}[/]" : $"[green]{Markup.Escape(Loc.LabelNo)}[/]");
            Present(t, G("query"));
            return;
        }
        catch (Exception ex) { lastError = ex; }

        // ── ipinfo.io — резервный, 50 000 req/мес без ключа ──────────────
        try
        {
            var json = await http.GetStringAsync($"https://ipinfo.io/{Uri.EscapeDataString(host)}/json");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string G(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            var ip = G("ip");
            if (string.IsNullOrEmpty(ip))
                throw new InvalidDataException(Loc.ErrAddressNotFound);
            var org     = G("org");
            var asNum   = org.Length > 0 ? org.Split(' ', 2)[0] : "";
            var orgName = org.Contains(' ') ? org.Split(' ', 2)[1] : "";

            var t = BuildTable();
            Row(t, Loc.ColIpAddress, ip);
            Row(t, Loc.LabelCountry, G("country"));
            Row(t, Loc.LabelRegion,  G("region"));
            Row(t, Loc.LabelCity,    G("city"));
            Row(t, Loc.LabelOrg,     orgName);
            Row(t, "AS",             asNum);
            Row(t, "rDNS",        G("hostname"));
            Present(t, ip);
            return;
        }
        catch (Exception ex) { lastError = ex; }

        // ── ipwho.is — последний резерв ───────────────────────────────────
        try
        {
            var json = await http.GetStringAsync($"https://ipwho.is/{Uri.EscapeDataString(host)}");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                throw new InvalidDataException(Loc.ErrAddressNotFound);

            string G(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            string GConn(string k)
            {
                if (!r.TryGetProperty("connection", out var conn)) return "";
                if (!conn.TryGetProperty(k, out var v))             return "";
                return v.ValueKind == JsonValueKind.Number ? v.GetInt64().ToString() : v.GetString() ?? "";
            }

            var ip  = G("ip");
            var asn = GConn("asn");
            var t = BuildTable();
            Row(t, Loc.ColIpAddress, ip);
            Row(t, Loc.LabelCountry, $"{G("country")} ({G("country_code")})");
            Row(t, Loc.LabelRegion,  G("region"));
            Row(t, Loc.LabelCity,    G("city"));
            Row(t, "ISP",            GConn("isp"));
            Row(t, Loc.LabelOrg,     GConn("org"));
            Row(t, "AS",             asn.Length > 0 && asn != "0" ? $"AS{asn}" : "");
            Row(t, Loc.LabelAsName,  GConn("domain"));
            Present(t, ip);
            return;
        }
        catch (Exception ex) { lastError = ex; }

        AnsiConsole.MarkupLine($"\n[red]{Markup.Escape(Loc.ErrorPrefix)}[/] {Markup.Escape(lastError?.Message ?? Loc.ErrAllSourcesUnavailable)}");
    });
    Pause();
}

async Task ToolReverseDnsAsync()
{
    ToolHeader(Loc.ToolReverseDns, Loc.ToolReverseDnsDesc);
    var ip = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskIpAddress)}[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(ip)) return;

    await AnsiConsole.Status().StartAsync(Loc.StatusPtrQuery, async _ =>
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]IP:[/] {Markup.Escape(ip)}");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Loc.LabelHost)}[/] [bold]{Markup.Escape(entry.HostName)}[/]");
            if (entry.Aliases.Length > 0)
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Loc.LabelAliases)}[/]");
                foreach (var a in entry.Aliases)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(a)}");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]{Markup.Escape(Loc.ErrorPrefix)}[/] {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}

async Task ToolPingAsync()
{
    ToolHeader(Loc.ToolPing, Loc.ToolPingDesc);
    var host = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskHostOrIp)}[/]").AllowEmpty()).Trim();
    if (string.IsNullOrWhiteSpace(host)) return;

    const int count = 4;
    var results = new List<(bool ok, long ms)>();

    await AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"[cyan]{Markup.Escape(Loc.PingLabel)} {Markup.Escape(host)}[/]", maxValue: count);
            using var ping = new System.Net.NetworkInformation.Ping();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 3000);
                    var ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                    results.Add((ok, ok ? reply.RoundtripTime : -1));
                    task.Description = ok
                        ? $"[cyan]{Markup.Escape(Loc.PingLabel)} {Markup.Escape(host)}[/]  [green]{reply.RoundtripTime} {Loc.UnitMs}[/]"
                        : $"[cyan]{Markup.Escape(Loc.PingLabel)} {Markup.Escape(host)}[/]  [red]{Markup.Escape(Loc.PingTimeout)}[/]";
                }
                catch (Exception ex)
                {
                    results.Add((false, -1));
                    task.Description = $"[cyan]{Markup.Escape(Loc.PingLabel)} {Markup.Escape(host)}[/]  [red]{Markup.Escape(ex.Message)}[/]";
                }
                task.Increment(1);
                if (i < count - 1) await Task.Delay(500);
            }
        });

    var ok2 = results.Where(r => r.ok).ToList();
    var loss = results.Count - ok2.Count;
    AnsiConsole.WriteLine();
    var t = new Table().Border(TableBorder.Rounded)
        .AddColumn($"[grey]{Markup.Escape(Loc.ColParam)}[/]").AddColumn(Loc.ColValue);
    t.AddRow($"[grey]{Markup.Escape(Loc.PingSent)}[/]",  count.ToString());
    t.AddRow($"[grey]{Markup.Escape(Loc.PingLost)}[/]",  loss == 0 ? "[green]0[/]" : $"[red]{loss}[/]");
    if (ok2.Count > 0)
    {
        t.AddRow($"[grey]{Markup.Escape(Loc.PingMin)}[/]",  $"[cyan]{ok2.Min(r => r.ms)} {Loc.UnitMs}[/]");
        t.AddRow($"[grey]{Markup.Escape(Loc.PingMax)}[/]",  $"[cyan]{ok2.Max(r => r.ms)} {Loc.UnitMs}[/]");
        t.AddRow($"[grey]{Markup.Escape(Loc.PingAvg)}[/]",  $"[cyan]{ok2.Average(r => r.ms):F0} {Loc.UnitMs}[/]");
    }
    AnsiConsole.Write(t);
    Pause();
}

async Task ToolPortCheckAsync()
{
    ToolHeader(Loc.ToolPortCheck, Loc.ToolPortCheckDesc);
    var hostInput = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskHostOrHostPort)}[/]").AllowEmpty()).Trim();
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
        var portStr = AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]{Markup.Escape(Loc.AskPort)}[/]").AllowEmpty()).Trim();
        if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out port)) return;
    }

    if (port < 1 || port > 65535) return;

    await AnsiConsole.Status().StartAsync($"{Loc.StatusChecking} {host}:{port}...", async _ =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await tcp.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(Loc.PortOpen)}[/]  [grey]{host}:{port}[/]  [cyan]{sw.ElapsedMilliseconds} {Loc.UnitMs}[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"\n[red]{Markup.Escape(Loc.PortTimeout)}[/]  [grey]{host}:{port}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]{Markup.Escape(Loc.PortClosed)}[/]  [grey]{host}:{port}[/]  {Markup.Escape(ex.Message)}");
        }
    });
    Pause();
}
