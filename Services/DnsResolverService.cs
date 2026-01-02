using System.Net;
using System.Net.Sockets;
using VpnConfigTester.Infrastructure;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для резолва доменных имен в IP адреса
/// </summary>
public sealed class DnsResolverService(ILogger? logger = null) : IDnsResolver
{
    private readonly Dictionary<string, IPAddress?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>
    /// Резолвит доменное имя в IP адрес (возвращает только один IPv4 адрес, как socket.gethostbyname в Python)
    /// </summary>
    public async Task<IPAddress?> ResolveAsync(string hostname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return null;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(hostname, out var cachedIp))
                return cachedIp;
        }
        finally
        {
            _cacheLock.Release();
        }

        if (IPAddress.TryParse(hostname, out var ipAddress))
        {
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                _cache[hostname] = ipAddress;
            }
            finally
            {
                _cacheLock.Release();
            }
            return ipAddress;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname);
            var ipv4Address = addresses
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                _cache[hostname] = ipv4Address;
            }
            finally
            {
                _cacheLock.Release();
            }

            if (ipv4Address != null)
                logger?.LogInfo($"Резолв {hostname} -> {ipv4Address}");
            else
                logger?.LogWarning($"Не удалось резолвить {hostname} (IPv4 адрес не найден)");

            return ipv4Address;
        }
        catch (SocketException ex)
        {
            logger?.LogWarning($"Ошибка DNS резолва для {hostname}: {ex.Message}");
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                _cache[hostname] = null;
            }
            finally
            {
                _cacheLock.Release();
            }
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Неожиданная ошибка при резолве {hostname}: {ex.Message}");
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                _cache[hostname] = null;
            }
            finally
            {
                _cacheLock.Release();
            }
            return null;
        }
    }

    /// <summary>
    /// Резолвит список доменных имен в IP адреса
    /// </summary>
    public async Task<Dictionary<string, IPAddress?>> ResolveBatchAsync(
        IEnumerable<string> hostnames,
        CancellationToken cancellationToken = default)
    {
        var uniqueHostnames = hostnames
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new Dictionary<string, IPAddress?>(StringComparer.OrdinalIgnoreCase);
        var tasks = uniqueHostnames.Select(async hostname =>
        {
            var ip = await ResolveAsync(hostname, cancellationToken);
            return (hostname, ip);
        });

        var resolved = await Task.WhenAll(tasks);
        foreach (var (hostname, ip) in resolved)
        {
            results[hostname] = ip;
        }

        return results;
    }
}

