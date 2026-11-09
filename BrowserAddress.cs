using System;

namespace ObiBrowser;

internal static class BrowserAddress
{
    internal const string Home = "about:blank";
    private const string Search = "https://duckduckgo.com/?q=";

    internal static bool TryResolve(string input, out Uri? address)
    {
        address = null;
        string text = input.Trim();
        if (text.Length == 0)
            return false;

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return TryWebAddress(text, out address);

        if (!HasWhitespace(text))
        {
            if (text.IndexOf("://", StringComparison.Ordinal) >= 0)
                return TryWebAddress(text, out address);

            if (TryWebAddress("https://" + text, out Uri? candidate) &&
                candidate != null && LooksLikeHost(candidate))
            {
                address = candidate.IsLoopback
                    ? new Uri("http://" + text)
                    : candidate;
                return true;
            }

            int colon = text.IndexOf(':');
            if (colon > 0 && Uri.CheckSchemeName(text.Substring(0, colon)))
                return false;
        }

        // Older .NET Framework versions limit EscapeDataString's input length.
        if (text.Length > 8192)
            return false;

        address = new Uri(Search + Uri.EscapeDataString(text));
        return true;
    }

    internal static bool TryWebAddress(string text, out Uri? address)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
            parsed.UserInfo.Length == 0 && parsed.HostNameType != UriHostNameType.Unknown &&
            parsed.HostNameType != UriHostNameType.Basic)
        {
            address = parsed;
            return true;
        }

        address = null;
        return false;
    }

    private static bool LooksLikeHost(Uri address) =>
        address.IsLoopback || address.HostNameType == UriHostNameType.IPv4 ||
        address.HostNameType == UriHostNameType.IPv6 || address.Host.IndexOf('.') > 0;

    private static bool HasWhitespace(string text)
    {
        foreach (char character in text)
            if (char.IsWhiteSpace(character))
                return true;
        return false;
    }
}
