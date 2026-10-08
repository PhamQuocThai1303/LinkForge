using UrlShortener.Domain;

namespace UrlShortener.UnitTests;

public class Base62Tests
{
    [Theory]
    [InlineData(0L, "0000000")]
    [InlineData(125L, "0000021")]
    [InlineData(100000000000L, "1L9zO9O")]
    [InlineData(3521614606207L, "ZZZZZZZ")]
    public void Seven_character_codes_round_trip(long number, string code)
    {
        Assert.Equal(code, Base62.EncodeSeven(number));
        Assert.True(Base62.TryDecode(code, out var decoded));
        Assert.Equal(number, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("🤖")]
    [InlineData("ZZZZZZZZZZZZ")]
    public void Invalid_codes_are_rejected(string code)
    {
        Assert.False(Base62.TryDecode(code, out _));
    }

    [Fact]
    public void Encoding_beyond_seven_digits_fails()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Base62.EncodeSeven(3521614606208L));
    }
}
