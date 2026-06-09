using System.Text;
using System.Text.Json;
using VpnCheck.Models;
using VpnCheck.Services;

namespace VpnCheck.Tests.Services;

public class SingBoxConfigBuilderTests
{
    private readonly SingBoxConfigBuilder _builder = new();

    private static ServerInfo MakeServer(string url, string host = "1.2.3.4", int port = 443, string protocol = "vless") =>
        new() { Host = host, Port = port, OriginalUrl = url, Protocol = protocol };

    // --- Guard clauses ---

    [Fact]
    public void TryBuildOutbound_NullServer_ReturnsFalse()
    {
        var result = _builder.TryBuildOutbound(null!, "tag", out _);
        Assert.False(result);
    }

    [Fact]
    public void TryBuildOutbound_EmptyUrl_ReturnsFalse()
    {
        var server = new ServerInfo { Host = "1.2.3.4", Port = 443, OriginalUrl = "", Protocol = "vless" };
        Assert.False(_builder.TryBuildOutbound(server, "tag", out _));
    }

    [Fact]
    public void TryBuildOutbound_UnknownProtocol_ReturnsFalse()
    {
        var server = MakeServer("wireguard://key@1.2.3.4:443");
        Assert.False(_builder.TryBuildOutbound(server, "tag", out _));
    }

    // --- VLESS ---

    [Fact]
    public void TryBuildOutbound_Vless_ReturnsCorrectOutbound()
    {
        var url = "vless://my-uuid@1.2.3.4:443?type=tcp&security=tls&sni=example.com";
        var server = MakeServer(url);

        var ok = _builder.TryBuildOutbound(server, "proxy-1", out var outbound);

        Assert.True(ok);
        Assert.Equal("vless", outbound["type"]);
        Assert.Equal("proxy-1", outbound["tag"]);
        Assert.Equal("1.2.3.4", outbound["server"]);
        Assert.Equal(443, outbound["server_port"]);
        Assert.Equal("my-uuid", outbound["uuid"]);
    }

    [Fact]
    public void TryBuildOutbound_Vless_WithTls_IncludesTlsBlock()
    {
        var url = "vless://uuid@host.com:443?security=tls&sni=host.com";
        var server = MakeServer(url);

        _builder.TryBuildOutbound(server, "t", out var outbound);

        Assert.True(outbound.ContainsKey("tls"));
        var tls = Assert.IsType<Dictionary<string, object?>>(outbound["tls"]);
        Assert.Equal(true, tls["enabled"]);
        Assert.Equal("host.com", tls["server_name"]);
    }

    [Fact]
    public void TryBuildOutbound_Vless_WithFlow_IncludesFlow()
    {
        var url = "vless://uuid@host.com:443?flow=xtls-rprx-vision";
        var server = MakeServer(url);

        _builder.TryBuildOutbound(server, "t", out var outbound);

        Assert.Equal("xtls-rprx-vision", outbound["flow"]);
    }

    [Fact]
    public void TryBuildOutbound_Vless_WithWebSocketTransport_IncludesTransport()
    {
        var url = "vless://uuid@host.com:443?type=ws&path=%2Fws&host=cdn.example.com";
        var server = MakeServer(url);

        _builder.TryBuildOutbound(server, "t", out var outbound);

        Assert.True(outbound.ContainsKey("transport"));
        var transport = Assert.IsType<Dictionary<string, object?>>(outbound["transport"]);
        Assert.Equal("ws", transport["type"]);
        Assert.Equal("/ws", transport["path"]);
    }

    // --- Trojan ---

    [Fact]
    public void TryBuildOutbound_Trojan_ReturnsCorrectOutbound()
    {
        var url = "trojan://my-password@5.6.7.8:443";
        var server = MakeServer(url, "5.6.7.8", 443, "trojan");

        var ok = _builder.TryBuildOutbound(server, "proxy-2", out var outbound);

        Assert.True(ok);
        Assert.Equal("trojan", outbound["type"]);
        Assert.Equal("my-password", outbound["password"]);
        Assert.Equal("5.6.7.8", outbound["server"]);
        Assert.Equal(443, outbound["server_port"]);
    }

    [Fact]
    public void TryBuildOutbound_Trojan_AlwaysHasTls()
    {
        var url = "trojan://pass@host.com:443";
        var server = MakeServer(url, protocol: "trojan");

        _builder.TryBuildOutbound(server, "t", out var outbound);

        Assert.True(outbound.ContainsKey("tls"));
        var tls = Assert.IsType<Dictionary<string, object?>>(outbound["tls"]);
        Assert.Equal(true, tls["enabled"]);
    }

    // --- VMess ---

    [Fact]
    public void TryBuildOutbound_Vmess_ReturnsCorrectOutbound()
    {
        var json = JsonSerializer.Serialize(new
        {
            add = "10.0.0.1",
            port = "8080",
            id = "test-uuid",
            net = "tcp",
            tls = ""
        });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var url = $"vmess://{base64}";
        var server = MakeServer(url, "10.0.0.1", 8080, "vmess");

        var ok = _builder.TryBuildOutbound(server, "proxy-vmess", out var outbound);

        Assert.True(ok);
        Assert.Equal("vmess", outbound["type"]);
        Assert.Equal("10.0.0.1", outbound["server"]);
        Assert.Equal(8080, outbound["server_port"]);
        Assert.Equal("test-uuid", outbound["uuid"]);
    }

    [Fact]
    public void TryBuildOutbound_Vmess_WithTlsAndWs_IncludesBothBlocks()
    {
        var json = JsonSerializer.Serialize(new
        {
            add = "10.0.0.1",
            port = "443",
            id = "uuid",
            net = "ws",
            tls = "tls",
            sni = "cdn.example.com",
            path = "/path"
        });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var url = $"vmess://{base64}";
        var server = MakeServer(url, "10.0.0.1", 443, "vmess");

        _builder.TryBuildOutbound(server, "t", out var outbound);

        Assert.True(outbound.ContainsKey("tls"));
        Assert.True(outbound.ContainsKey("transport"));
        var transport = Assert.IsType<Dictionary<string, object?>>(outbound["transport"]);
        Assert.Equal("ws", transport["type"]);
        Assert.Equal("/path", transport["path"]);
    }

    [Fact]
    public void TryBuildOutbound_Vmess_MissingRequiredFields_ReturnsFalse()
    {
        var json = JsonSerializer.Serialize(new { id = "uuid" });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var url = $"vmess://{base64}";
        var server = MakeServer(url, protocol: "vmess");

        var ok = _builder.TryBuildOutbound(server, "t", out _);
        Assert.False(ok);
    }

    // --- Hysteria2 ---

    [Fact]
    public void TryBuildOutbound_Hysteria2_ReturnsCorrectOutbound()
    {
        var url = "hysteria2://my-pass@server.com:443";
        var server = MakeServer(url, "server.com", 443, "hysteria2");

        var ok = _builder.TryBuildOutbound(server, "proxy-h2", out var outbound);

        Assert.True(ok);
        Assert.Equal("hysteria2", outbound["type"]);
        Assert.Equal("server.com", outbound["server"]);
        Assert.Equal(443, outbound["server_port"]);
        Assert.Equal("my-pass", outbound["password"]);
    }

    // --- Hysteria ---

    [Fact]
    public void TryBuildOutbound_Hysteria_ReturnsCorrectOutbound()
    {
        var url = "hysteria://server.com:8443?auth_str=secret&upmbps=100&downmbps=200";
        var server = MakeServer(url, "server.com", 8443, "hysteria");

        var ok = _builder.TryBuildOutbound(server, "proxy-h", out var outbound);

        Assert.True(ok);
        Assert.Equal("hysteria", outbound["type"]);
        Assert.Equal(100, outbound["up_mbps"]);
        Assert.Equal(200, outbound["down_mbps"]);
        Assert.Equal("secret", outbound["auth_str"]);
    }

    // --- Reality TLS ---

    [Fact]
    public void TryBuildOutbound_VlessWithReality_IncludesRealityBlock()
    {
        var url = "vless://uuid@host.com:443?security=reality&sni=host.com&pbk=pubkey123&sid=shortid";
        var server = MakeServer(url);

        _builder.TryBuildOutbound(server, "t", out var outbound);

        var tls = Assert.IsType<Dictionary<string, object?>>(outbound["tls"]);
        Assert.True(tls.ContainsKey("reality"));
        var reality = Assert.IsType<Dictionary<string, object?>>(tls["reality"]);
        Assert.Equal("pubkey123", reality["public_key"]);
        Assert.Equal("shortid", reality["short_id"]);
    }
}
