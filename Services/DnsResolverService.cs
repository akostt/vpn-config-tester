using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnCheck.Infrastructure;

namespace VpnCheck.Services;

/// <summary>
/// Реализация сервиса для резолва доменных имен в IP адреса
/// </summary>
public sealed class DnsResolverService(ILogger? logger = null) : IDnsResolver
{
    private const int MaxConcurrentDnsResolutions = 128;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
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

            if (ipv4Address == null)
                _logger.LogInfo($"DNS: нет IPv4 для {hostname}");

            return ipv4Address;
        }
        catch (SocketException ex)
        {
            _logger.LogInfo($"DNS: не резолвится {hostname}: {ex.Message}");
            _cache[hostname] = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogInfo($"DNS: ошибка {hostname}: {ex.Message}");
            _cache[hostname] = null;
            return null;
        }
    }

    /// <summary>
    /// Резолвит список доменных имен в IP адреса
    /// </summary>
    public async Task<Dictionary<string, IPAddress?>> ResolveBatchAsync(
        IEnumerable<string> hostnames,
        Action<int, int, int>? onProgress = null,
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

        var done = 0;
        var resolved = 0;

        await Parallel.ForEachAsync(uniqueHostnames, parallelOptions, async (hostname, ct) =>
        {
            var ip = await ResolveAsync(hostname, ct);
            results[hostname] = ip;

            var d = Interlocked.Increment(ref done);
            if (ip != null) Interlocked.Increment(ref resolved);
            onProgress?.Invoke(d, uniqueHostnames.Count, Volatile.Read(ref resolved));
        });

        return new Dictionary<string, IPAddress?>(results, StringComparer.OrdinalIgnoreCase);
    }
}
