using System.Text.Json;
using System.Text.Json.Serialization;
using VpnCheck.Services;

namespace VpnCheck;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class VpnCheckJsonContext : JsonSerializerContext
{
    internal static readonly VpnCheckJsonContext Indented =
        new(new JsonSerializerOptions { WriteIndented = true });
}
