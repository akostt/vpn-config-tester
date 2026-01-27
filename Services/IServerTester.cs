using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

public interface IServerTester
{
    Task<IReadOnlyList<ServerInfo>> TestServersAsync(
        IReadOnlyList<ServerInfo> servers,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);

    Task<HashSet<string>> TestUniqueIpsAsync(
        IEnumerable<string> uniqueIps,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

