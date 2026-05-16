using VpnConfigTester.Models;

namespace VpnConfigTester.Services;

public interface IServerTester
{
    Task<IReadOnlyList<ServerInfo>> TestServersAsync(
        IReadOnlyList<ServerInfo> servers,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);

    Task<HashSet<string>> TestUniqueEndpointsAsync(
        IEnumerable<(string IpAddress, int Port)> endpoints,
        Action<int, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);
}
