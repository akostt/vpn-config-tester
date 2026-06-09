using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VpnCheck.Localization;

public static class Loc
{
    private static readonly Lazy<Dictionary<string, string>> _strings =
        new(LoadStrings, LazyThreadSafetyMode.PublicationOnly);

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

    // ── Loader ─────────────────────────────────────────────────────────────
    private static Dictionary<string, string> LoadStrings()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
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
            var r = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
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
                var r = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (r != null) return r;
            }
            catch { }
        }

        throw new InvalidOperationException("Localization resource not found in assembly.");
    }
}
