using System.Net;

namespace UrlShortener.Application;

public static class UrlValidator
{
    public const int MaximumLength = 2048;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength ||
            value.Any(char.IsControl) || value != value.Trim() ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 ||
            uri.Host.Length == 0 ||
            !uri.Host.Contains('.') ||
            Uri.CheckHostName(uri.Host) != UriHostNameType.Dns ||
            IPAddress.TryParse(uri.Host, out _))
        {
            return false;
        }

        return true;
    }
}
