using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VpnCheck.Localization;

public static class Loc
{
    private static string? _overrideLang;
    private static Lazy<Dictionary<string, string>> _strings =
        new(LoadStrings, LazyThreadSafetyMode.PublicationOnly);

    public static void Reload(string? language = null)
    {
        _overrideLang = language is "auto" or null or "" ? null : language;
        _strings = new Lazy<Dictionary<string, string>>(LoadStrings, LazyThreadSafetyMode.PublicationOnly);
    }

    public static string Get(string key) =>
        _strings.Value.TryGetValue(key, out var v) ? v : key;

    // ── Typed accessors ────────────────────────────────────────────────────
    public static string MenuRun            => Get("MenuRun");
    public static string MenuLocal          => Get("MenuLocal");
    public static string MenuAnalyze        => Get("MenuAnalyze");
    public static string MenuSources        => Get("MenuSources");
    public static string MenuSettings       => Get("MenuSettings");
    public static string MenuExit           => Get("MenuExit");
    public static string MenuTitle          => Get("MenuTitle");
    public static string SourcesTitle       => Get("SourcesTitle");
    public static string SettingsTitle      => Get("SettingsTitle");
    public static string ActionAdd          => Get("ActionAdd");
    public static string ActionAddServer    => Get("ActionAddServer");
    public static string ActionRemove       => Get("ActionRemove");
    public static string ActionBack         => Get("ActionBack");
    public static string AskUrl             => Get("AskUrl");
    public static string AskUri             => Get("AskUri");
    public static string AskRemoveNum       => Get("AskRemoveNum");
    public static string Added              => Get("Added");
    public static string Removed            => Get("Removed");
    public static string Cancelled          => Get("Cancelled");
    public static string NotFound           => Get("NotFound");
    public static string InvalidUri         => Get("InvalidUri");
    public static string PressAnyKey        => Get("PressAnyKey");
    public static string CriticalError      => Get("CriticalError");
    public static string SettingTcpTimeout  => Get("SettingTcpTimeout");
    public static string SettingConcurrent  => Get("SettingConcurrent");
    public static string SettingHttpTimeout => Get("SettingHttpTimeout");
    public static string SettingSingBox     => Get("SettingSingBox");
    public static string SettingSingBoxTimeout    => Get("SettingSingBoxTimeout");
    public static string SettingSingBoxConcurrent => Get("SettingSingBoxConcurrent");
    public static string Saved              => Get("Saved");
    public static string SingBoxOn          => Get("SingBoxOn");
    public static string SingBoxOff         => Get("SingBoxOff");
    public static string ColNumber          => Get("ColNumber");
    public static string ColSource          => Get("ColSource");
    public static string ColType            => Get("ColType");
    public static string TypeSubscription   => Get("TypeSubscription");
    public static string TypeServer         => Get("TypeServer");
    public static string ColParam           => Get("ColParam");
    public static string ColValue           => Get("ColValue");
    public static string AskValue           => Get("AskValue");
    public static string ActionChange       => Get("ActionChange");
    public static string MenuTools          => Get("MenuTools");
    public static string ToolsTitle         => Get("ToolsTitle");
    public static string ToolDnsLookup      => Get("ToolDnsLookup");
    public static string ToolMyIp           => Get("ToolMyIp");
    public static string ToolIpInfo         => Get("ToolIpInfo");
    public static string ToolReverseDns     => Get("ToolReverseDns");
    public static string ToolPortCheck      => Get("ToolPortCheck");
    public static string ToolPing           => Get("ToolPing");
    public static string SettingLogLevel    => Get("SettingLogLevel");
    public static string LogLevelError      => Get("LogLevelError");
    public static string LogLevelWarning    => Get("LogLevelWarning");
    public static string LogLevelAll        => Get("LogLevelAll");
    public static string LogLevelNone       => Get("LogLevelNone");
    public static string SettingLanguage    => Get("SettingLanguage");
    public static string LangRu             => Get("LangRu");
    public static string LangEn             => Get("LangEn");
    public static string LangAuto           => Get("LangAuto");
    public static string IpRangesEmpty          => Get("IpRangesEmpty");
    public static string AnalyzeSelectFile      => Get("AnalyzeSelectFile");
    public static string AnalyzeFileOutput      => Get("AnalyzeFileOutput");
    public static string AnalyzeFileServers     => Get("AnalyzeFileServers");
    public static string AnalyzeFileCustom      => Get("AnalyzeFileCustom");
    public static string AnalyzeAskPath         => Get("AnalyzeAskPath");
    public static string SourcesChangedTitle    => Get("SourcesChangedTitle");
    public static string ColChange              => Get("ColChange");
    public static string SaveChangesPrompt      => Get("SaveChangesPrompt");
    public static string ActionSave             => Get("ActionSave");
    public static string ActionDiscard          => Get("ActionDiscard");
    public static string MenuExport             => Get("MenuExport");
    public static string ExportTitle            => Get("ExportTitle");
    public static string ExportNoData           => Get("ExportNoData");
    public static string ExportFormatPlain      => Get("ExportFormatPlain");
    public static string ExportFormatBase64     => Get("ExportFormatBase64");
    public static string ExportFormatSingBox    => Get("ExportFormatSingBox");
    public static string ExportFormatTitle      => Get("ExportFormatTitle");
    public static string ExportPathPrompt       => Get("ExportPathPrompt");
    public static string ExportDone             => Get("ExportDone");
    public static string ExportError            => Get("ExportError");
    public static string ToolsSubtitle          => Get("ToolsSubtitle");
    public static string UnitServers            => Get("UnitServers");
    public static string ToolDnsLookupDesc      => Get("ToolDnsLookupDesc");
    public static string ToolMyIpDesc           => Get("ToolMyIpDesc");
    public static string ToolIpInfoDesc         => Get("ToolIpInfoDesc");
    public static string ToolReverseDnsDesc     => Get("ToolReverseDnsDesc");
    public static string ToolPingDesc           => Get("ToolPingDesc");
    public static string ToolPortCheckDesc      => Get("ToolPortCheckDesc");
    public static string AskDomain              => Get("AskDomain");
    public static string AskIpOrDomain          => Get("AskIpOrDomain");
    public static string AskIpAddress           => Get("AskIpAddress");
    public static string AskHostOrIp            => Get("AskHostOrIp");
    public static string AskHostOrHostPort      => Get("AskHostOrHostPort");
    public static string AskPort                => Get("AskPort");
    public static string StatusResolving        => Get("StatusResolving");
    public static string StatusGettingIp        => Get("StatusGettingIp");
    public static string StatusQuerying         => Get("StatusQuerying");
    public static string StatusPtrQuery         => Get("StatusPtrQuery");
    public static string StatusChecking         => Get("StatusChecking");
    public static string StatusUnavailable      => Get("StatusUnavailable");
    public static string ColVersion             => Get("ColVersion");
    public static string ColAddress             => Get("ColAddress");
    public static string ColIpAddress           => Get("ColIpAddress");
    public static string MyIpTitle              => Get("MyIpTitle");
    public static string IpInfoTitle            => Get("IpInfoTitle");
    public static string LabelCountry           => Get("LabelCountry");
    public static string LabelRegion            => Get("LabelRegion");
    public static string LabelCity              => Get("LabelCity");
    public static string LabelOrg               => Get("LabelOrg");
    public static string LabelAsName            => Get("LabelAsName");
    public static string LabelProxyVpn          => Get("LabelProxyVpn");
    public static string LabelHosting           => Get("LabelHosting");
    public static string LabelHost              => Get("LabelHost");
    public static string LabelAliases           => Get("LabelAliases");
    public static string LabelYes               => Get("LabelYes");
    public static string LabelNo                => Get("LabelNo");
    public static string ErrorPrefix            => Get("ErrorPrefix");
    public static string ErrAddressNotFound     => Get("ErrAddressNotFound");
    public static string ErrAllSourcesUnavailable => Get("ErrAllSourcesUnavailable");
    public static string PingLabel              => Get("PingLabel");
    public static string PingTimeout            => Get("PingTimeout");
    public static string UnitMs                 => Get("UnitMs");
    public static string PingSent               => Get("PingSent");
    public static string PingLost               => Get("PingLost");
    public static string PingMin                => Get("PingMin");
    public static string PingMax                => Get("PingMax");
    public static string PingAvg                => Get("PingAvg");
    public static string PortOpen               => Get("PortOpen");
    public static string PortTimeout            => Get("PortTimeout");
    public static string PortClosed             => Get("PortClosed");

    // ── Loader ─────────────────────────────────────────────────────────────
    private static Dictionary<string, string> LoadStrings()
    {
        var culture = _overrideLang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var assembly = Assembly.GetExecutingAssembly();

        // 1. Embedded resource (translators: add strings.xx.json as EmbeddedResource)
        foreach (var name in new[]
        {
            $"VpnCheck.Localization.strings.{culture}.json",
            "VpnCheck.Localization.strings.ru.json"
        })
        {
            var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var r = JsonSerializer.Deserialize(reader.ReadToEnd(), VpnCheckJsonContext.Default.DictionaryStringString);
            if (r != null) return r;
        }

        // 2. File next to executable (translators: drop strings.xx.json beside VPNCheck)
        var baseDir = AppContext.BaseDirectory;
        foreach (var path in new[]
        {
            Path.Combine(baseDir, $"strings.{culture}.json"),
            Path.Combine(baseDir, "strings.ru.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var r = JsonSerializer.Deserialize(File.ReadAllText(path), VpnCheckJsonContext.Default.DictionaryStringString);
                if (r != null) return r;
            }
            catch { }
        }

        throw new InvalidOperationException("Localization resource not found in assembly.");
    }
}
