using System.Text;
using System.Text.Json;
using VpnCheck.Services;

namespace VpnCheck.Tests.Services;

public class ServerParserTests
{
    private readonly ServerParser _parser = new();

    private static IEnumerable<(string Line, string SourceUrl)> Lines(params string[] lines) =>
        lines.Select(l => (l, "https://test.example.com/config"));

    // --- Null / empty inputs ---

    [Fact]
    public void ParseServers_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _parser.ParseServers(null!));
    }

    [Fact]
    public void ParseServers_EmptyInput_ReturnsEmpty()
    {
        var result = _parser.ParseServers(Lines());
        Assert.Empty(result);
    }

    [Fact]
    public void ParseServers_BlankLines_AreSkipped()
    {
        var result = _parser.ParseServers(Lines("", "   ", "\t"));
        Assert.Empty(result);
    }

    // --- VLESS ---

    [Fact]
    public void ParseServers_ValidVless_ParsesHostAndPort()
    {
        var url = "vless://uuid@1.2.3.4:443?type=tcp";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("1.2.3.4", result[0].Host);
        Assert.Equal(443, result[0].Port);
        Assert.Equal("vless", result[0].Protocol);
        Assert.Equal(url, result[0].OriginalUrl);
    }

    [Fact]
    public void ParseServers_VlessWithDomain_ParsesCorrectly()
    {
        var url = "vless://uuid@example.com:8443?security=tls";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("example.com", result[0].Host);
        Assert.Equal(8443, result[0].Port);
    }

    [Fact]
    public void ParseServers_VlessWithIpv6_ParsesCorrectly()
    {
        var url = "vless://uuid@[::1]:443";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("::1", result[0].Host);
        Assert.Equal(443, result[0].Port);
    }

    [Fact]
    public void ParseServers_VlessMissingUserInfo_Skipped()
    {
        var result = _parser.ParseServers(Lines("vless://1.2.3.4:443"));
        Assert.Empty(result);
    }

    // --- Trojan ---

    [Fact]
    public void ParseServers_ValidTrojan_ParsesHostAndPort()
    {
        var url = "trojan://password@5.6.7.8:443";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("5.6.7.8", result[0].Host);
        Assert.Equal(443, result[0].Port);
        Assert.Equal("trojan", result[0].Protocol);
    }

    // --- VMess ---

    [Fact]
    public void ParseServers_ValidVmess_ParsesHostAndPort()
    {
        var json = JsonSerializer.Serialize(new { add = "10.0.0.1", port = 8080 });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var url = $"vmess://{base64}";

        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("10.0.0.1", result[0].Host);
        Assert.Equal(8080, result[0].Port);
        Assert.Equal("vmess", result[0].Protocol);
    }

    [Fact]
    public void ParseServers_VmessInvalidBase64_Skipped()
    {
        var result = _parser.ParseServers(Lines("vmess://!!!notbase64!!!"));
        Assert.Empty(result);
    }

    // --- Shadowsocks SIP002 ---

    [Fact]
    public void ParseServers_ShadowsocksSIP002_DoesNotThrow()
    {
        var url = "ss://Y2hhY2hhMjAtaWV0Zi1wb2x5MTMwNTpwYXNzd29yZA==@192.168.1.1:8388";
        var result = _parser.ParseServers(Lines(url));
        Assert.NotNull(result);
    }

    // --- Hysteria2 ---

    [Fact]
    public void ParseServers_ValidHysteria2_ParsesHostAndPort()
    {
        var url = "hysteria2://password@my.server.com:443";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("my.server.com", result[0].Host);
        Assert.Equal(443, result[0].Port);
        Assert.Equal("hysteria2", result[0].Protocol);
    }

    // --- Hysteria ---

    [Fact]
    public void ParseServers_ValidHysteria_ParsesHostAndPort()
    {
        var url = "hysteria://my.server.com:8443";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("my.server.com", result[0].Host);
        Assert.Equal(8443, result[0].Port);
        Assert.Equal("hysteria", result[0].Protocol);
    }

    [Fact]
    public void ParseServers_HysteriaWithUserInfo_ParsesHostCorrectly()
    {
        var url = "hysteria://auth@my.server.com:8443";
        var result = _parser.ParseServers(Lines(url));

        Assert.Single(result);
        Assert.Equal("my.server.com", result[0].Host);
    }

    // --- SourceConfigUrl propagation ---

    [Fact]
    public void ParseServers_SourceUrl_IsPropagatedToResult()
    {
        var url = "vless://uuid@1.2.3.4:443";
        var source = "https://my-sub.example.com/sub";
        var result = _parser.ParseServers(new[] { (url, source) });

        Assert.Single(result);
        Assert.Equal(source, result[0].SourceConfigUrl);
    }

    // --- Port validation ---

    [Fact]
    public void ParseServers_PortZero_Skipped()
    {
        var result = _parser.ParseServers(Lines("vless://uuid@host.com:0"));
        Assert.Empty(result);
    }

    [Fact]
    public void ParseServers_PortTooHigh_Skipped()
    {
        var result = _parser.ParseServers(Lines("vless://uuid@host.com:99999"));
        Assert.Empty(result);
    }

    // --- Line too long ---

    [Fact]
    public void ParseServers_LineTooLong_Skipped()
    {
        var longLine = "vless://uuid@host.com:443?" + new string('x', 2100);
        var result = _parser.ParseServers(Lines(longLine));
        Assert.Empty(result);
    }

    // --- Mixed valid and invalid ---

    [Fact]
    public void ParseServers_MixedLines_ReturnsOnlyValid()
    {
        var lines = Lines(
            "# comment",
            "vless://uuid@1.2.3.4:443",
            "not-a-url",
            "trojan://pass@5.6.7.8:443"
        );
        var result = _parser.ParseServers(lines);
        Assert.Equal(2, result.Count);
    }
}
