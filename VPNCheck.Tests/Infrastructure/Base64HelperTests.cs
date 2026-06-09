using VpnCheck.Infrastructure;

namespace VpnCheck.Tests.Infrastructure;

public class Base64HelperTests
{
    // --- Decode ---

    [Fact]
    public void Decode_NullInput_ReturnsNull()
    {
        Assert.Null(Base64Helper.Decode(null));
    }

    [Fact]
    public void Decode_EmptyInput_ReturnsNull()
    {
        Assert.Null(Base64Helper.Decode(""));
    }

    [Fact]
    public void Decode_WhitespaceInput_ReturnsNull()
    {
        Assert.Null(Base64Helper.Decode("   "));
    }

    [Fact]
    public void Decode_ValidStandardBase64_ReturnsDecodedString()
    {
        var encoded = Convert.ToBase64String("hello world"u8.ToArray());
        Assert.Equal("hello world", Base64Helper.Decode(encoded));
    }

    [Fact]
    public void Decode_UrlSafeDashes_DecodesCorrectly()
    {
        var urlSafe = Convert.ToBase64String("hello"u8.ToArray()).Replace('+', '-').Replace('/', '_');
        Assert.Equal("hello", Base64Helper.Decode(urlSafe));
    }

    [Fact]
    public void Decode_MissingPadding_DecodesCorrectly()
    {
        var noPadding = Convert.ToBase64String("hello"u8.ToArray()).TrimEnd('=');
        Assert.Equal("hello", Base64Helper.Decode(noPadding));
    }

    [Fact]
    public void Decode_PadLengthOne_ReturnsNull()
    {
        // length % 4 == 1 is always invalid in base64
        Assert.Null(Base64Helper.Decode("a"));
    }

    [Fact]
    public void Decode_InvalidBase64Chars_ReturnsNull()
    {
        Assert.Null(Base64Helper.Decode("!!!invalid!!!"));
    }

    [Fact]
    public void Decode_Utf8Content_RoundTrips()
    {
        var original = "конфиг VPN";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original));
        Assert.Equal(original, Base64Helper.Decode(encoded));
    }

    // --- IsLikelyEncoded ---

    [Fact]
    public void IsLikelyEncoded_ShortString_ReturnsFalse()
    {
        Assert.False(Base64Helper.IsLikelyEncoded("abc"));
    }

    [Fact]
    public void IsLikelyEncoded_LengthMod4IsOne_ReturnsFalse()
    {
        // 33 chars — length % 4 == 1
        Assert.False(Base64Helper.IsLikelyEncoded(new string('A', 33)));
    }

    [Fact]
    public void IsLikelyEncoded_ValidBase64Chars_ReturnsTrue()
    {
        Assert.True(Base64Helper.IsLikelyEncoded(new string('A', 32)));
    }

    [Fact]
    public void IsLikelyEncoded_ContainsNonBase64Chars_ReturnsFalse()
    {
        var withSpaces = new string('A', 28) + "    ";
        Assert.False(Base64Helper.IsLikelyEncoded(withSpaces));
    }

    // --- PrepareForDecode ---

    [Fact]
    public void PrepareForDecode_StripNewlines_RemovesAllWhitespace()
    {
        Assert.Equal("aGVsbG8=", Base64Helper.PrepareForDecode("aGVs\nbG8=\r\n"));
    }

    [Fact]
    public void PrepareForDecode_UrlSafeChars_ConvertsToStandard()
    {
        Assert.Equal("a+b/c", Base64Helper.PrepareForDecode("a-b_c"));
    }

    [Fact]
    public void PrepareForDecode_StripsTabs_AndSpaces()
    {
        Assert.Equal("aGVsbG8=", Base64Helper.PrepareForDecode("aGVs\t bG8="));
    }
}
