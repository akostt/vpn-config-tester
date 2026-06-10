using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

/// <summary>
/// Проверка доступности конфигов через sing-box
/// </summary>
public sealed class SingBoxTester(
    ApplicationConfiguration config,
    SingBoxConfigBuilder configBuilder,
    ILogger? logger = null) : ISingBoxTester
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly SingBoxConfigBuilder _configBuilder = configBuilder ?? throw new ArgumentNullException(nameof(configBuilder));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<IReadOnlyList<ServerInfo>> TestAsync(
        IReadOnlyList<ServerInfo> servers,
        string singBoxPath,
        Action<int, int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (servers == null || servers.Count == 0)
            return Array.Empty<ServerInfo>();

        if (string.IsNullOrWhiteSpace(singBoxPath) || !File.Exists(singBoxPath))
        {
            _logger.LogWarning("sing-box не найден, пропускаю проверку.");
            return Array.Empty<ServerInfo>();
        }

        var successful = new ConcurrentBag<ServerInfo>();
        var tested = 0;

        _logger.LogInfo("");
        _logger.LogInfo($"Проверка через sing-box (максимум {_config.MaxConcurrentSingBoxTests} параллельных потоков)...");

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _config.MaxConcurrentSingBoxTests,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(servers, parallelOptions, async (server, ct) =>
        {
            var tag = $"proxy-{Guid.NewGuid():N}";
            string? configPath = null;

            if (!_configBuilder.TryBuildOutbound(server, tag, out var outbound))
            {
                Interlocked.Increment(ref tested);
                onProgress?.Invoke(tested, servers.Count, successful.Count);
                return;
            }

            try
            {
                var portReservation = ReservePort();
                var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
                configPath = await CreateTempConfigAsync(outbound, tag, port, ct);

                var success = await RunSingBoxAndTestAsync(singBoxPath, configPath, portReservation, ct);
                if (success)
                    successful.Add(server);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"sing-box: ошибка {server.Protocol}: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(configPath))
                    TryDelete(configPath);
            }

            var t = Interlocked.Increment(ref tested);
            onProgress?.Invoke(t, servers.Count, successful.Count);
        });

        _logger.LogInfo($"sing-box проверка завершена: {successful.Count} из {servers.Count} успешны");
        return successful.ToList().AsReadOnly();
    }

    private async Task<bool> RunSingBoxAndTestAsync(string singBoxPath, string configPath, System.Net.Sockets.TcpListener portReservation, CancellationToken cancellationToken)
    {
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = singBoxPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("run");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(configPath);

        try
        {
            portReservation.Stop();
            if (!process.Start())
                return false;

            var ready = await WaitForPortAsync(port, TimeSpan.FromSeconds(3), cancellationToken);
            if (!ready)
                return false;

            return await TestUrlViaProxyAsync(port, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Ошибка запуска sing-box: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task<bool> TestUrlViaProxyAsync(int proxyPort, CancellationToken cancellationToken)
    {
        var proxy = new WebProxy($"http://127.0.0.1:{proxyPort}");
        using var handler = new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.SingBoxTestTimeoutSeconds)
        };

        try
        {
            var response = await httpClient.GetAsync(_config.SingBoxTestUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> CreateTempConfigAsync(
        JsonObject outbound,
        string outboundTag,
        int listenPort,
        CancellationToken cancellationToken)
    {
        var inbound = new JsonObject
        {
            ["type"] = "mixed",
            ["tag"] = "mixed-in",
            ["listen"] = "127.0.0.1",
            ["listen_port"] = listenPort
        };
        var directOutbound = new JsonObject { ["type"] = "direct", ["tag"] = "direct" };
        var rule = new JsonObject
        {
            ["inbound"] = new JsonArray((JsonNode?)"mixed-in"),
            ["action"] = "route",
            ["outbound"] = outboundTag
        };
        var configObject = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn" },
            ["inbounds"] = new JsonArray((JsonNode?)inbound),
            ["outbounds"] = new JsonArray((JsonNode?)outbound, (JsonNode?)directOutbound),
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray((JsonNode?)rule)
            }
        };

        var json = configObject.ToJsonString();

        var tempPath = Path.Combine(Path.GetTempPath(), $"singbox_test_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);

        // Restrict temp config access to owner only — file may contain VPN credentials
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* non-fatal */ }
        }

        return tempPath;
    }

    private static System.Net.Sockets.TcpListener ReservePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(200, cancellationToken));
                if (completed == connectTask && client.Connected)
                    return true;
            }
            catch
            {
                // ignore
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // ignore
        }
    }
}
