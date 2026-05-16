using System.Collections.Concurrent;
using System.Net.Sockets;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

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

        var successfulServers = new List<ServerInfo>();
        var semaphore = new SemaphoreSlim(_config.MaxConcurrentTests);
        var tasks = new List<Task>();
        var lockObject = new object();

        var tested = 0;
        var total = servers.Count;

        logger?.LogInfo($"Начинаю тестирование {total} серверов...");

        foreach (var server in servers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    var isReachable = await TestTcpConnectionAsync(server.Host, server.Port, cancellationToken);

                    if (isReachable)
                    {
                        lock (lockObject)
                        {
                            successfulServers.Add(server);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Ошибка при тестировании {server.Host}:{server.Port}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();

                    lock (lockObject)
                    {
                        tested++;
                        progressCallback?.Invoke(tested, total, successfulServers.Count);
                    }
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        logger?.LogInfo($"Тестирование завершено: {successfulServers.Count} из {total} серверов доступны");

        return successfulServers;
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
                logger?.LogWarning($"Ошибка при TCP тестировании {endpoint.IpAddress}:{endpoint.Port}: {ex.Message}");
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
            logger?.LogWarning($"Ошибка TCP подключения к {host}:{port}: {ex.Message}");
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
