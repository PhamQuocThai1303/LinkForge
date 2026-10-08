using UrlShortener.Application;

namespace UrlShortener.UnitTests;

public class UrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://example.com/")]
    public void Accepts_http_urls(string url)
    {
        Assert.True(UrlValidator.IsValid(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://example.com/\r\nX-Test: injected")]
    public void Rejects_invalid_or_unsafe_urls(string url)
    {
        Assert.False(UrlValidator.IsValid(url));
    }

    [Fact]
    public void Rejects_long_url()
    {
        Assert.False(UrlValidator.IsValid("https://example.com/" + new string('a', 2048)));
    }
}
