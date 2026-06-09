using System.Collections.Concurrent;
using System.Net.Sockets;
using VpnCheck.Infrastructure;
using VpnCheck.Models;

namespace VpnCheck.Services;

public sealed class ServerTester(ApplicationConfiguration config, ILogger? logger = null) : IServerTester
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<IReadOnlyList<ServerInfo>> TestServersAsync(
        IReadOnlyList<ServerInfo> servers,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (servers == null || servers.Count == 0)
            return Array.Empty<ServerInfo>();

        var successful = new ConcurrentBag<ServerInfo>();
        var tested = 0;

        logger?.LogInfo($"Начинаю тестирование {servers.Count} серверов...");

        await Parallel.ForEachAsync(servers,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _config.MaxConcurrentTests), CancellationToken = cancellationToken },
            async (server, ct) =>
            {
                try
                {
                    if (await TestTcpConnectionAsync(server.Host, server.Port, ct))
                        successful.Add(server);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"TCP: ошибка {server.Host}:{server.Port}: {ex.Message}");
                }
                finally
                {
                    var t = Interlocked.Increment(ref tested);
                    progressCallback?.Invoke(t, servers.Count, successful.Count);
                }
            });

        var result = successful.ToList();
        logger?.LogInfo($"Тестирование завершено: {result.Count} из {servers.Count} серверов доступны");
        return result;
    }

    public async Task<HashSet<string>> TestUniqueEndpointsAsync(
        IEnumerable<(string IpAddress, int Port)> endpoints,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var endpointList = endpoints
            .Where(e => !string.IsNullOrWhiteSpace(e.IpAddress) && IsValidPort(e.Port))
            .Select(e => (IpAddress: e.IpAddress.Trim(), e.Port))
            .Distinct()
            .ToList();

        if (endpointList.Count == 0)
            return new HashSet<string>();

        var successfulEndpoints = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var tested = 0;
        var total = endpointList.Count;

        logger?.LogInfo($"Начинаю TCP тестирование {total} уникальных IP:port...");

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _config.MaxConcurrentTests),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(endpointList, parallelOptions, async (endpoint, ct) =>
        {
            try
            {
                if (await TestTcpConnectionAsync(endpoint.IpAddress, endpoint.Port, ct))
                    successfulEndpoints.TryAdd(BuildEndpointKey(endpoint.IpAddress, endpoint.Port), 0);
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"TCP: ошибка {endpoint.IpAddress}:{endpoint.Port}: {ex.Message}");
            }
            finally
            {
                var currentTested = Interlocked.Increment(ref tested);
                progressCallback?.Invoke(currentTested, total, successfulEndpoints.Count);
            }
        });

        logger?.LogInfo($"TCP тестирование завершено: {successfulEndpoints.Count} из {total} IP:port доступны");

        return new HashSet<string>(successfulEndpoints.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> TestTcpConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!IsValidPort(port))
            return false;

        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(host, port, cancellationToken).AsTask();
            var timeoutTask = Task.Delay(_config.TcpTimeoutMs, cancellationToken);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await connectTask;
            return tcpClient.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"TCP: неожиданная ошибка {host}:{port}: {ex.Message}");
            return false;
        }
    }

    public static string BuildEndpointKey(string ipAddress, int port)
    {
        var host = ipAddress.Contains(':') ? $"[{ipAddress}]" : ipAddress;
        return $"{host}:{port}";
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;
}
