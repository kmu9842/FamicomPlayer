using System;

namespace FamicomPlayer;

internal static class BrowserNavigationPolicy
{
    internal static bool IsAllowed(string address, bool allowBlank = false)
    {
        if (allowBlank && address == "about:blank") return true;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        return uri.IdnHost.ToLowerInvariant() switch
        {
            "www.youtube.com" or "youtube.com" or "m.youtube.com" or "music.youtube.com" => true,
            // Google hands the verified session back through accounts.youtube.com.
            // Cancelling this navigation leaves the verification page waiting forever.
            "accounts.google.com" or "accounts.youtube.com" or "consent.google.com" or "consent.youtube.com" => true,
            "www.google.com" => uri.AbsolutePath.StartsWith("/accounts/", StringComparison.Ordinal),
            _ => false
        };
    }

    internal static string SafeHost(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.IdnHost : "";
}
