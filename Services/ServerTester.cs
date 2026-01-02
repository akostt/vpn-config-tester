using System.Net.Sockets;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для тестирования доступности серверов
/// </summary>
public sealed class ServerTester(ApplicationConfiguration config, ILogger? logger = null) : IServerTester
{
    private readonly ApplicationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Тестирует доступность серверов через TCP подключение
    /// </summary>
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

