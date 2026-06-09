namespace VpnCheck.Models;

public sealed class ApplicationConfiguration
{
    public string[] SubscriptionUrls { get; init; } = [];
    public string[] CustomServers { get; init; } = [];
    public bool SingBoxEnabled { get; init; } = true;

    public string SourceConfigFile { get; init; } = "source_config.txt";
    public string SuccessfulServersFile { get; init; } = "successful_servers.txt";
    public string OutputConfigFile { get; init; } = "output_config.txt";
    public string IpRangesFile { get; init; } = "ip_ranges.txt";

    public int TcpTimeoutMs { get; init; } = 3000;
    public int MaxConcurrentTests { get; init; } = 256;
    public int HttpTimeoutSeconds { get; init; } = 30;

    public string SingBoxTestUrl { get; init; } = "http://cp.cloudflare.com/";
    public string SingBoxToolsDirectory { get; init; } = "tools/sing-box";
    public string? SingBoxExecutablePath { get; init; }
    public int SingBoxTestTimeoutSeconds { get; init; } = 10;
    public int MaxConcurrentSingBoxTests { get; init; } = 50;

    public int MinIpCountForSubnet24 { get; init; } = 3;
    public int MinIpCountForSubnet16 { get; init; } = 5;
}
