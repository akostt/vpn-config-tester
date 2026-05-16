using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnConfigTester.Infrastructure;

namespace VpnConfigTester.Services;

/// <summary>
/// Реализация сервиса для резолва доменных имен в IP адреса
/// </summary>
public sealed class DnsResolverService(ILogger? logger = null) : IDnsResolver
{
    private const int MaxConcurrentDnsResolutions = 128;
    private readonly ConcurrentDictionary<string, IPAddress?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Резолвит доменное имя в IP адрес (возвращает только один IPv4 адрес, как socket.gethostbyname в Python)
    /// </summary>
    public async Task<IPAddress?> ResolveAsync(string hostname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return null;

        if (_cache.TryGetValue(hostname, out var cachedIp))
            return cachedIp;

        if (IPAddress.TryParse(hostname, out var ipAddress))
        {
            _cache[hostname] = ipAddress;
            return ipAddress;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);
            var ipv4Address = addresses
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            _cache[hostname] = ipv4Address;

            if (ipv4Address != null)
                logger?.LogInfo($"Резолв {hostname} -> {ipv4Address}");
            else
                logger?.LogWarning($"Не удалось резолвить {hostname} (IPv4 адрес не найден)");

            return ipv4Address;
        }
        catch (SocketException ex)
        {
            logger?.LogWarning($"Ошибка DNS резолва для {hostname}: {ex.Message}");
            _cache[hostname] = null;
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Неожиданная ошибка при резолве {hostname}: {ex.Message}");
            _cache[hostname] = null;
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

        var results = new ConcurrentDictionary<string, IPAddress?>(StringComparer.OrdinalIgnoreCase);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentDnsResolutions,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(uniqueHostnames, parallelOptions, async (hostname, ct) =>
        {
            var ip = await ResolveAsync(hostname, ct);
            results[hostname] = ip;
        });

        return new Dictionary<string, IPAddress?>(results, StringComparer.OrdinalIgnoreCase);
    }
}
