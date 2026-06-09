namespace VpnCheck.Infrastructure;

internal static class VpnProtocols
{
    public const string Vless = "vless";
    public const string Trojan = "trojan";
    public const string VMess = "vmess";
    public const string Shadowsocks = "shadowsocks";
    public const string Hysteria = "hysteria";
    public const string Hysteria2 = "hysteria2";

    public const string VlessScheme = "vless://";
    public const string TrojanScheme = "trojan://";
    public const string VMessScheme = "vmess://";
    public const string ShadowsocksScheme = "ss://";
    public const string HysteriaScheme = "hysteria://";
    public const string Hysteria2Scheme = "hysteria2://";

    public static readonly string[] AllSchemes =
    [
        VlessScheme, TrojanScheme, VMessScheme, ShadowsocksScheme, HysteriaScheme, Hysteria2Scheme
    ];

    public static bool IsVpnUrl(string line) =>
        AllSchemes.Any(s => line.StartsWith(s, StringComparison.OrdinalIgnoreCase));
}
