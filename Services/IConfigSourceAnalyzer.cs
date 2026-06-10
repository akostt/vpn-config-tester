using VpnCheck.Models;

namespace VpnCheck.Services;

public interface IConfigSourceAnalyzer
{
    IReadOnlyList<ConfigSourceStats> AnalyzeSources(
        IReadOnlyList<ServerInfo> allServers,
        IReadOnlyList<ServerInfo> successfulServers);

    void PrintSubscriptionRanking(IReadOnlyList<ConfigSourceStats> stats);
}

