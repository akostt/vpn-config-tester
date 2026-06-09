using VpnCheck.Models;
using VpnCheck.Services;

namespace VpnCheck.Tests.Services;

public sealed class IpRangeAnalyzerServiceTests : IDisposable
{
    private readonly ApplicationConfiguration _config = new()
    {
        MinIpCountForSubnet24 = 3,
        MinIpCountForSubnet16 = 5
    };

    private readonly List<string> _tempFiles = [];

    private IpRangeAnalyzerService MakeService() => new(_config);

    private string WriteTempFile(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best-effort cleanup */ }
    }

    // --- Guard clauses ---

    [Fact]
    public void AnalyzeIpRanges_NullPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeService().AnalyzeIpRanges(null!));
    }

    [Fact]
    public void AnalyzeIpRanges_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeService().AnalyzeIpRanges(""));
    }

    [Fact]
    public void AnalyzeIpRanges_NonExistentFile_ReturnsEmpty()
    {
        var result = MakeService().AnalyzeIpRanges("/tmp/does-not-exist-xyz.txt");
        Assert.Empty(result);
    }

    // --- Parsing: plain IPs ---

    [Fact]
    public void AnalyzeIpRanges_EmptyFile_ReturnsEmpty()
    {
        var file = WriteTempFile();
        Assert.Empty(MakeService().AnalyzeIpRanges(file));
    }

    [Fact]
    public void AnalyzeIpRanges_BlankLines_AreSkipped()
    {
        var file = WriteTempFile("", "   ", "\t");
        Assert.Empty(MakeService().AnalyzeIpRanges(file));
    }

    [Fact]
    public void AnalyzeIpRanges_ValidIpLines_ParsedCorrectly()
    {
        var file = WriteTempFile("1.2.3.4", "1.2.3.5", "1.2.3.6");
        var result = MakeService().AnalyzeIpRanges(file);
        Assert.Single(result);
        Assert.Equal("1.2.3.0/24", result[0].Cidr);
        Assert.Equal(3, result[0].Count);
    }

    // --- Parsing: endpoint format (host:port) ---

    [Fact]
    public void AnalyzeIpRanges_EndpointFormat_ExtractsHost()
    {
        var file = WriteTempFile("10.0.0.1:443", "10.0.0.2:443", "10.0.0.3:80");
        var result = MakeService().AnalyzeIpRanges(file);
        Assert.Single(result);
        Assert.Equal("10.0.0.0/24", result[0].Cidr);
        Assert.Equal(3, result[0].Count);
    }

    // --- /24 grouping ---

    [Fact]
    public void AnalyzeIpRanges_TwoSubnet24Groups_BothPresent()
    {
        var file = WriteTempFile(
            "1.2.3.1", "1.2.3.2", "1.2.3.3",
            "5.6.7.1", "5.6.7.2", "5.6.7.3"
        );
        var result = MakeService().AnalyzeIpRanges(file);
        var cidrs = result.Select(r => r.Cidr).ToList();
        Assert.Contains("1.2.3.0/24", cidrs);
        Assert.Contains("5.6.7.0/24", cidrs);
    }

    [Fact]
    public void AnalyzeIpRanges_OrderedByCountDescending()
    {
        var file = WriteTempFile(
            "2.0.0.1",
            "1.0.0.1", "1.0.0.2", "1.0.0.3"
        );
        var result = MakeService().AnalyzeIpRanges(file);
        Assert.Equal("1.0.0.0/24", result[0].Cidr);
    }

    // --- /16 grouping ---

    [Fact]
    public void AnalyzeIpRanges_EnoughIpsFor16_AddsSubnet16()
    {
        // 5 IPs in different /24s, same /16 (10.1.x.x), none in 10.1.0.x
        // so the /24 check doesn't block the /16 entry
        var file = WriteTempFile(
            "10.1.1.1", "10.1.2.1", "10.1.3.1", "10.1.4.1", "10.1.5.1"
        );
        var result = MakeService().AnalyzeIpRanges(file);
        Assert.Contains(result, r => r.Cidr == "10.1.0.0/16");
    }

    [Fact]
    public void AnalyzeIpRanges_NotEnoughIpsFor16_NoSubnet16()
    {
        // Only 4 IPs in same /16 — below MinIpCountForSubnet16 = 5
        var file = WriteTempFile("10.1.0.1", "10.1.1.1", "10.1.2.1", "10.1.3.1");
        var result = MakeService().AnalyzeIpRanges(file);
        Assert.DoesNotContain(result, r => r.Cidr.EndsWith("/16"));
    }

    // --- Min/Max IP ordering ---

    [Fact]
    public void AnalyzeIpRanges_MinMaxIp_AreCorrect()
    {
        var file = WriteTempFile("1.2.3.5", "1.2.3.1", "1.2.3.9");
        var result = MakeService().AnalyzeIpRanges(file);
        var range = result.First(r => r.Cidr == "1.2.3.0/24");
        Assert.Equal("1.2.3.1", range.MinIp.ToString());
        Assert.Equal("1.2.3.9", range.MaxIp.ToString());
    }

    // --- SaveRangesToFile ---

    [Fact]
    public void SaveRangesToFile_WritesExpectedContent()
    {
        var ipFile = WriteTempFile("1.2.3.1", "1.2.3.2", "1.2.3.3");
        var outFile = Path.GetTempFileName();
        _tempFiles.Add(outFile);

        var service = MakeService();
        var ranges = service.AnalyzeIpRanges(ipFile);
        service.SaveRangesToFile(ranges, outFile);

        var content = File.ReadAllText(outFile);
        Assert.Contains("1.2.3.0/24", content);
    }

    [Fact]
    public void SaveRangesToFile_NullRanges_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MakeService().SaveRangesToFile(null!, "/tmp/out.txt"));
    }

    [Fact]
    public void SaveRangesToFile_EmptyOutputPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => MakeService().SaveRangesToFile([], ""));
    }
}
