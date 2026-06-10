using Spectre.Console;
using VpnCheck.Infrastructure;
using VpnCheck.Localization;
using VpnCheck.Models;

namespace VpnCheck.Services;

public sealed class ConfigSourceAnalyzer(ILogger? logger = null) : IConfigSourceAnalyzer
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public IReadOnlyList<ConfigSourceStats> AnalyzeSources(
        IReadOnlyList<ServerInfo> allServers,
        IReadOnlyList<ServerInfo> successfulServers)
    {
        if (allServers == null || allServers.Count == 0)
            return Array.Empty<ConfigSourceStats>();

        var successfulSet = new HashSet<string>(
            successfulServers.Select(s => $"{s.GetIpAddressOrHost()}:{s.Port}"));

        var sourceGroups = allServers
            .GroupBy(s => string.IsNullOrWhiteSpace(s.SourceConfigUrl) ? "local" : s.SourceConfigUrl);

        var stats = new List<ConfigSourceStats>();

        foreach (var group in sourceGroups)
        {
            var sourceUrl = group.Key;
            var servers = group.ToList();

            var uniqueServers = servers
                .GroupBy(s => $"{s.GetIpAddressOrHost()}:{s.Port}")
                .Select(g => g.First())
                .ToList();

            var successful = uniqueServers
                .Count(s => successfulSet.Contains($"{s.GetIpAddressOrHost()}:{s.Port}"));

            stats.Add(new ConfigSourceStats
            {
                SourceUrl = sourceUrl,
                TotalServers = servers.Count,
                UniqueServers = uniqueServers.Count,
                SuccessfulServers = successful
            });
        }

        return stats.OrderByDescending(s => s.QualityScore).ToList();
    }

    public void PrintSubscriptionRanking(IReadOnlyList<ConfigSourceStats> stats)
    {
        if (stats == null || stats.Count == 0) return;

        var subs = stats
            .Where(s => s.SourceUrl != "local" && s.SourceUrl != "custom")
            .OrderByDescending(s => s.QualityScore)
            .ToList();

        if (subs.Count == 0) return;

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(Color.Grey);
        table.AddColumn(new TableColumn("[grey]#[/]").RightAligned());
        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(Loc.SubRankSource)}[/]"));
        table.AddColumn(new TableColumn($"[grey]{Markup.Escape(Loc.SubRankTotal)}[/]").RightAligned());
        table.AddColumn(new TableColumn($"[grey]{Markup.Escape(Loc.SubRankUnique)}[/]").RightAligned());
        table.AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Loc.SubRankOk)}[/]").RightAligned());
        table.AddColumn(new TableColumn($"[grey]{Markup.Escape(Loc.SubRankRate)}[/]").RightAligned());

        for (int i = 0; i < subs.Count; i++)
        {
            var s = subs[i];
            var rank = i + 1;
            var shortUrl = ShortenUrl(s.SourceUrl, 42);
            var rateStr = s.UniqueServers > 0 ? $"{s.SuccessRatePercent:F0}%" : "—";

            var rankMarkup = rank switch
            {
                1 => "[bold yellow]1[/]",
                2 => "[bold white]2[/]",
                3 => "[white]3[/]",
                _ => $"[grey]{rank}[/]"
            };

            string nameColor, okColor;
            if (s.SuccessfulServers == 0)
            {
                nameColor = "grey";
                okColor = "grey";
            }
            else if (s.SuccessRatePercent >= 25 || rank <= 3)
            {
                nameColor = "white";
                okColor = "green";
            }
            else if (s.SuccessRatePercent >= 10 || s.SuccessfulServers >= 5)
            {
                nameColor = "white";
                okColor = "yellow";
            }
            else
            {
                nameColor = "grey";
                okColor = "grey";
            }

            table.AddRow(
                rankMarkup,
                $"[{nameColor}]{Markup.Escape(shortUrl)}[/]",
                $"[grey]{s.TotalServers}[/]",
                $"[grey]{s.UniqueServers}[/]",
                $"[{okColor}]{s.SuccessfulServers}[/]",
                $"[{(s.SuccessfulServers > 0 ? okColor : "grey")}]{Markup.Escape(rateStr)}[/]"
            );
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Loc.SubRankTitle)}[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.Write(table);

        var topSubs = subs.Where(s => s.SuccessfulServers > 0).Take(3).ToList();
        if (topSubs.Count > 0)
        {
            var medals = new[] { "[yellow]★[/]", "[white]★[/]", "[grey]★[/]" };
            var parts = topSubs.Select((s, i) =>
                $"{medals[i]} [green]{Markup.Escape(ShortenUrl(s.SourceUrl, 30))}[/] [grey]({s.SuccessfulServers} ok, {s.SuccessRatePercent:F0}%)[/]"
            );
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(Loc.SubRankBest)}:[/]  {string.Join("  ", parts)}");
            AnsiConsole.WriteLine();
        }
    }

    private static string ShortenUrl(string url, int maxLength)
    {
        if (string.IsNullOrEmpty(url) || url.Length <= maxLength)
            return url;

        var shortened = url.Replace("https://", "").Replace("http://", "");
        if (shortened.Length <= maxLength)
            return shortened;

        return shortened[..(maxLength - 3)] + "...";
    }
}
