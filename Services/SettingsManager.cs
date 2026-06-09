using System.Text.Json;

namespace VpnCheck.Services;

public sealed class AppSettings
{
    public int TcpTimeoutMs { get; set; } = 3000;
    public int MaxConcurrentTests { get; set; } = 256;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public bool SingBoxEnabled { get; set; } = true;
    public string SingBoxToolsDir { get; set; } = "tools/sing-box";
    public int SingBoxTimeoutSeconds { get; set; } = 10;
    public int MaxConcurrentSingBoxTests { get; set; } = 50;
    public int MinIpCountForSubnet24 { get; set; } = 3;
    public int MinIpCountForSubnet16 { get; set; } = 5;
    // none | error | warning | all
    public string LogLevel { get; set; } = "error";
    // auto | ru | en
    public string Language { get; set; } = "auto";
}

public static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load(string filePath)
    {
        if (!File.Exists(filePath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(string filePath, AppSettings settings) =>
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOpts));

    public static void EnsureDefaultFile(string filePath)
    {
        if (!File.Exists(filePath))
            Save(filePath, new AppSettings());
    }
}
