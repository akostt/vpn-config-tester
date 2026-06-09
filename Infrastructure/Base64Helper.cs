using System.Text;

namespace VpnCheck.Infrastructure;

internal static class Base64Helper
{
    public static string? Decode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Replace('-', '+').Replace('_', '/');
        var pad = normalized.Length % 4;
        if (pad == 1)
            return null;
        if (pad > 0)
            normalized = normalized.PadRight(normalized.Length + (4 - pad), '=');

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch
        {
            return null;
        }
    }

    public static bool IsLikelyEncoded(string content)
    {
        if (content.Length < 32 || content.Length % 4 == 1)
            return false;

        return content.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '-' or '_');
    }

    public static string PrepareForDecode(string content) =>
        string.Concat(content.Where(c => c != '\n' && c != '\r' && c != ' ' && c != '\t'))
              .Replace('-', '+')
              .Replace('_', '/');
}
