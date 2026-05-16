using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

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

    public async Task<IReadOnlyList<ServerInfo>> TestAsync(
        IReadOnlyList<ServerInfo> servers,
        string singBoxPath,
        CancellationToken cancellationToken = default)
    {
        if (servers == null || servers.Count == 0)
            return Array.Empty<ServerInfo>();

        if (string.IsNullOrWhiteSpace(singBoxPath) || !File.Exists(singBoxPath))
        {
            logger?.LogWarning("sing-box не найден, пропускаю проверку.");
            return Array.Empty<ServerInfo>();
        }

        var successful = new ConcurrentBag<ServerInfo>();
        var tested = 0;
        var testedLock = new object();

        logger?.LogInfo("");
        logger?.LogInfo($"Проверка через sing-box (максимум {_config.MaxConcurrentSingBoxTests} параллельных потоков)...");

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
                logger?.LogWarning($"sing-box: не удалось построить outbound для {server.Protocol}.");
                return;
            }

            try
            {
                var port = GetFreePort();
                configPath = await CreateTempConfigAsync(outbound, tag, port, ct);

                var success = await RunSingBoxAndTestAsync(singBoxPath, configPath, port, ct);
                if (success)
                    successful.Add(server);

                lock (testedLock)
                {
                    tested++;
                    logger?.LogInfo($"sing-box: {tested}/{servers.Count} проверено, успешных: {successful.Count}");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"sing-box: ошибка при проверке {server.Protocol}: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(configPath))
                    TryDelete(configPath);
            }
        });

        logger?.LogInfo($"sing-box проверка завершена: {successful.Count} из {servers.Count} успешны");
        return successful.ToList().AsReadOnly();
    }

    private async Task<bool> RunSingBoxAndTestAsync(string singBoxPath, string configPath, int port, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = singBoxPath,
            Arguments = $"run -c \"{configPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            if (!process.Start())
                return false;

            var ready = await WaitForPortAsync(port, TimeSpan.FromSeconds(3), cancellationToken);
            if (!ready)
                return false;

            return await TestUrlViaProxyAsync(port, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка запуска sing-box: {ex.Message}");
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
        Dictionary<string, object?> outbound,
        string outboundTag,
        int listenPort,
        CancellationToken cancellationToken)
    {
        var configObject = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?> { ["level"] = "warn" },
            ["inbounds"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = listenPort
                }
            },
            ["outbounds"] = new List<object>
            {
                outbound,
                new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["rules"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["inbound"] = new[] { "mixed-in" },
                        ["action"] = "route",
                        ["outbound"] = outboundTag
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(configObject, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var tempPath = Path.Combine(Path.GetTempPath(), $"singbox_test_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        return tempPath;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
