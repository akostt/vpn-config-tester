using System.Net.NetworkInformation;
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

    public async Task<HashSet<string>> TestUniqueIpsAsync(
        IEnumerable<string> uniqueIps,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var ipList = uniqueIps.ToList();
        if (ipList.Count == 0)
            return new HashSet<string>();

        var successfulIps = new HashSet<string>();
        var semaphore = new SemaphoreSlim(_config.MaxConcurrentTests);
        var tasks = new List<Task>();
        var lockObject = new object();

        var tested = 0;
        var total = ipList.Count;

        logger?.LogInfo($"Начинаю ICMP ping тестирование {total} уникальных IP адресов...");

        foreach (var ip in ipList)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    var isReachable = await PingIpAsync(ip, cancellationToken);

                    if (isReachable)
                    {
                        lock (lockObject)
                        {
                            successfulIps.Add(ip);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Ошибка при пинге {ip}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();

                    lock (lockObject)
                    {
                        tested++;
                        progressCallback?.Invoke(tested, total, successfulIps.Count);
                    }
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        logger?.LogInfo($"Ping завершен: {successfulIps.Count} из {total} IP адресов доступны");

        return successfulIps;
    }

    private async Task<bool> PingIpAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var timeout = _config.TcpTimeoutMs;
            
            var reply = await ping.SendPingAsync(ipAddress, timeout);
            
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Ошибка ICMP ping к {ipAddress}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TestTcpConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(_config.TcpTimeoutMs, cancellationToken);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

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
}

