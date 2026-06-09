using System.Text.Json;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Построение outbounds для sing-box из ссылок
/// </summary>
public sealed class SingBoxConfigBuilder(ILogger? logger = null)
{
    public bool TryBuildOutbound(ServerInfo server, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (server == null || string.IsNullOrWhiteSpace(server.OriginalUrl))
            return false;

        try
        {
            var url = server.OriginalUrl;
            if (url.StartsWith(VpnProtocols.VlessScheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildVless(url, tag, out outbound);
            if (url.StartsWith(VpnProtocols.TrojanScheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildTrojan(url, tag, out outbound);
            if (url.StartsWith(VpnProtocols.VMessScheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildVmess(url, tag, out outbound);
            if (url.StartsWith(VpnProtocols.ShadowsocksScheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildShadowsocks(url, tag, out outbound);
            if (url.StartsWith(VpnProtocols.Hysteria2Scheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildHysteria2(url, tag, out outbound);
            if (url.StartsWith(VpnProtocols.HysteriaScheme, StringComparison.OrdinalIgnoreCase))
                return TryBuildHysteria(url, tag, out outbound);
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка построения sing-box outbounds: {ex.Message}");
        }

        return false;
    }

    private bool TryBuildVless(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var uuid = uri.UserInfo;
        if (string.IsNullOrWhiteSpace(uuid))
            return false;

        var query = ParseQuery(uri.Query);
        var tls = BuildTlsOptions(query);
        var transport = BuildTransportOptions(query);

        outbound["type"] = "vless";
        outbound["tag"] = tag;
        outbound["server"] = uri.Host;
        outbound["server_port"] = uri.Port;
        outbound["uuid"] = uuid;

        if (query.TryGetValue("flow", out var flow) && !string.IsNullOrWhiteSpace(flow))
            outbound["flow"] = flow;

        if (tls != null)
            outbound["tls"] = tls;
        if (transport != null)
            outbound["transport"] = transport;

        return true;
    }

    private bool TryBuildTrojan(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var password = uri.UserInfo;
        if (string.IsNullOrWhiteSpace(password))
            return false;

        var query = ParseQuery(uri.Query);
        var tls = BuildTlsOptions(query, true);
        var transport = BuildTransportOptions(query);

        outbound["type"] = "trojan";
        outbound["tag"] = tag;
        outbound["server"] = uri.Host;
        outbound["server_port"] = uri.Port;
        outbound["password"] = password;

        if (tls != null)
            outbound["tls"] = tls;
        if (transport != null)
            outbound["transport"] = transport;

        return true;
    }

    private bool TryBuildVmess(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        var base64 = url.Substring(VpnProtocols.VMessScheme.Length);
        var json = Base64Helper.Decode(base64);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var host = root.TryGetProperty("add", out var addProp) ? addProp.GetString() : null;
        var portStr = root.TryGetProperty("port", out var portProp) ? portProp.GetString() : null;
        var uuid = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var net = root.TryGetProperty("net", out var netProp) ? netProp.GetString() : null;
        var tlsMode = root.TryGetProperty("tls", out var tlsProp) ? tlsProp.GetString() : null;
        var sni = root.TryGetProperty("sni", out var sniProp) ? sniProp.GetString() : null;
        var path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
        var hostHeader = root.TryGetProperty("host", out var hostProp) ? hostProp.GetString() : null;
        var security = root.TryGetProperty("scy", out var scyProp) ? scyProp.GetString() : null;
        var grpcService = root.TryGetProperty("serviceName", out var snProp) ? snProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(portStr) || string.IsNullOrWhiteSpace(uuid))
            return false;

        if (!int.TryParse(portStr, out var port))
            return false;

        outbound["type"] = "vmess";
        outbound["tag"] = tag;
        outbound["server"] = host;
        outbound["server_port"] = port;
        outbound["uuid"] = uuid;

        if (!string.IsNullOrWhiteSpace(security))
            outbound["security"] = security;

        if (string.Equals(tlsMode, "tls", StringComparison.OrdinalIgnoreCase))
        {
            var tls = new Dictionary<string, object?> { ["enabled"] = true };
            if (!string.IsNullOrWhiteSpace(sni))
                tls["server_name"] = sni;
            outbound["tls"] = tls;
        }

        if (string.Equals(net, "ws", StringComparison.OrdinalIgnoreCase))
        {
            var ws = new Dictionary<string, object?> { ["type"] = "ws" };
            if (!string.IsNullOrWhiteSpace(path)) ws["path"] = path;
            if (!string.IsNullOrWhiteSpace(hostHeader))
            {
                ws["headers"] = new Dictionary<string, object?> { ["Host"] = hostHeader };
            }
            outbound["transport"] = ws;
        }
        else if (string.Equals(net, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            var grpc = new Dictionary<string, object?> { ["type"] = "grpc" };
            if (!string.IsNullOrWhiteSpace(grpcService))
                grpc["service_name"] = grpcService;
            outbound["transport"] = grpc;
        }

        return true;
    }

    private bool TryBuildShadowsocks(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return TryBuildShadowsocksManual(url, tag, out outbound);
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("plugin", out var plugin) && !string.IsNullOrWhiteSpace(plugin))
        {
            logger?.LogWarning("Shadowsocks plugin не поддерживается, пропуск.");
            return false;
        }

        var userInfo = uri.UserInfo;
        string method;
        string password;

        if (userInfo.Contains(':'))
        {
            var parts = userInfo.Split(':', 2);
            method = parts[0];
            password = parts[1];
        }
        else
        {
            var decoded = Base64Helper.Decode(userInfo);
            if (string.IsNullOrWhiteSpace(decoded) || !decoded.Contains(':'))
                return false;
            var parts = decoded.Split(':', 2);
            method = parts[0];
            password = parts[1];
        }

        outbound["type"] = "shadowsocks";
        outbound["tag"] = tag;
        outbound["server"] = uri.Host;
        outbound["server_port"] = uri.Port;
        outbound["method"] = method;
        outbound["password"] = password;

        return true;
    }

    private bool TryBuildShadowsocksManual(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        var raw = url.Substring(VpnProtocols.ShadowsocksScheme.Length);
        var hashIndex = raw.IndexOf('#');
        if (hashIndex >= 0)
            raw = raw.Substring(0, hashIndex);

        string decoded;
        if (raw.Contains('@'))
        {
            decoded = raw;
        }
        else
        {
            decoded = Base64Helper.Decode(raw) ?? string.Empty;
        }

        var atIndex = decoded.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= decoded.Length - 1)
            return false;

        var methodPass = decoded.Substring(0, atIndex);
        var hostPort = decoded.Substring(atIndex + 1);

        var colonIndex = methodPass.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= methodPass.Length - 1)
            return false;

        var method = methodPass.Substring(0, colonIndex);
        var password = methodPass.Substring(colonIndex + 1);

        string host;
        int port;

        var lastColon = hostPort.LastIndexOf(':');
        if (lastColon <= 0 || lastColon >= hostPort.Length - 1)
            return false;

        host = hostPort.Substring(0, lastColon);
        if (!int.TryParse(hostPort.Substring(lastColon + 1), out port))
            return false;

        outbound["type"] = "shadowsocks";
        outbound["tag"] = tag;
        outbound["server"] = host;
        outbound["server_port"] = port;
        outbound["method"] = method;
        outbound["password"] = password;

        return true;
    }

    private bool TryBuildHysteria2(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var query = ParseQuery(uri.Query);
        var password = uri.UserInfo;
        if (string.IsNullOrWhiteSpace(password) && query.TryGetValue("auth", out var auth))
            password = auth;

        outbound["type"] = "hysteria2";
        outbound["tag"] = tag;
        outbound["server"] = uri.Host;
        outbound["server_port"] = uri.Port;
        if (!string.IsNullOrWhiteSpace(password))
            outbound["password"] = password;

        var tls = BuildTlsOptions(query, true);
        if (tls != null)
            outbound["tls"] = tls;

        return true;
    }

    private bool TryBuildHysteria(string url, string tag, out Dictionary<string, object?> outbound)
    {
        outbound = new Dictionary<string, object?>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var query = ParseQuery(uri.Query);

        outbound["type"] = "hysteria";
        outbound["tag"] = tag;
        outbound["server"] = uri.Host;
        outbound["server_port"] = uri.Port;

        if (query.TryGetValue("auth", out var auth) && !string.IsNullOrWhiteSpace(auth))
            outbound["auth"] = auth;
        if (query.TryGetValue("auth_str", out var authStr) && !string.IsNullOrWhiteSpace(authStr))
            outbound["auth_str"] = authStr;
        if (query.TryGetValue("upmbps", out var up) && int.TryParse(up, out var upMbps))
            outbound["up_mbps"] = upMbps;
        if (query.TryGetValue("downmbps", out var down) && int.TryParse(down, out var downMbps))
            outbound["down_mbps"] = downMbps;

        var tls = BuildTlsOptions(query, true);
        if (tls != null)
            outbound["tls"] = tls;

        return true;
    }

    private Dictionary<string, object?>? BuildTlsOptions(Dictionary<string, string> query, bool alwaysEnable = false)
    {
        var hasTls = alwaysEnable;
        if (query.TryGetValue("security", out var security))
        {
            if (string.Equals(security, "tls", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(security, "reality", StringComparison.OrdinalIgnoreCase))
            {
                hasTls = true;
            }
        }

        if (!hasTls && !query.ContainsKey("sni") && !query.ContainsKey("fp"))
            return null;

        var tls = new Dictionary<string, object?> { ["enabled"] = true };
        if (query.TryGetValue("sni", out var sni) && !string.IsNullOrWhiteSpace(sni))
            tls["server_name"] = sni;

        if (query.TryGetValue("allowInsecure", out var allowInsecure) && allowInsecure == "1")
            tls["insecure"] = true;
        if (query.TryGetValue("insecure", out var insecure) && insecure == "1")
            tls["insecure"] = true;

        if (query.TryGetValue("fp", out var fp) && !string.IsNullOrWhiteSpace(fp))
        {
            tls["utls"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["fingerprint"] = fp
            };
        }

        if (query.TryGetValue("security", out var securityMode) &&
            string.Equals(securityMode, "reality", StringComparison.OrdinalIgnoreCase))
        {
            var reality = new Dictionary<string, object?> { ["enabled"] = true };
            if (query.TryGetValue("pbk", out var pbk) && !string.IsNullOrWhiteSpace(pbk))
                reality["public_key"] = pbk;
            if (query.TryGetValue("sid", out var sid) && !string.IsNullOrWhiteSpace(sid))
                reality["short_id"] = sid;
            if (query.TryGetValue("spx", out var spx) && !string.IsNullOrWhiteSpace(spx))
                reality["spider_x"] = spx;
            tls["reality"] = reality;
        }

        return tls;
    }

    private Dictionary<string, object?>? BuildTransportOptions(Dictionary<string, string> query)
    {
        if (!query.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
            return null;

        if (string.Equals(type, "ws", StringComparison.OrdinalIgnoreCase))
        {
            var ws = new Dictionary<string, object?> { ["type"] = "ws" };
            if (query.TryGetValue("path", out var path) && !string.IsNullOrWhiteSpace(path))
                ws["path"] = path;
            if (query.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
                ws["headers"] = new Dictionary<string, object?> { ["Host"] = host };
            return ws;
        }

        if (string.Equals(type, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            var grpc = new Dictionary<string, object?> { ["type"] = "grpc" };
            if (query.TryGetValue("serviceName", out var serviceName) && !string.IsNullOrWhiteSpace(serviceName))
                grpc["service_name"] = serviceName;
            return grpc;
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.TrimStart('?');
        var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

}
