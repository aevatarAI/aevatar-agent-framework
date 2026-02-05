using System.Net;
using System.Net.Sockets;

namespace Aevatar.VibeResearching.UserProviders.Application.Services;

/// <summary>
/// SSRF protection: validates that user-supplied endpoint URLs do not resolve
/// to private, loopback, link-local, or cloud-metadata IP addresses.
///
/// Two-layer design:
///   1. <see cref="RejectBlockedHostname"/> — synchronous hostname blocklist (fast, no DNS)
///   2. <see cref="EnsureSafeAsync"/> — async DNS resolution + IP range check (comprehensive)
/// </summary>
public static class EndpointSafetyGuard
{
    /// <summary>Hostnames that are always blocked regardless of IP resolution.</summary>
    private static readonly string[] BlockedExactHosts =
    {
        "localhost",
        "kubernetes.default.svc",
        "kubernetes.default",
        "metadata.google.internal"
    };

    /// <summary>DNS suffixes that indicate internal/cluster-local addresses.</summary>
    private static readonly string[] BlockedSuffixes =
    {
        ".local",
        ".internal",
        ".svc",
        ".svc.cluster.local"
    };

    // ================================================================
    //  Layer 1: Synchronous hostname check (for early validation)
    // ================================================================

    /// <summary>
    /// Fast synchronous check — rejects obviously internal hostnames.
    /// Call this during input validation for immediate user feedback.
    /// </summary>
    public static void RejectBlockedHostname(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return; // Let the normal URL validator handle malformed URLs

        var host = uri.Host;

        foreach (var blocked in BlockedExactHosts)
        {
            if (host.Equals(blocked, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Endpoint targets a blocked internal hostname.");
        }

        foreach (var suffix in BlockedSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Endpoint targets a blocked internal hostname.");
        }

        // Catch raw IP literals (e.g., http://169.254.169.254/)
        if (IPAddress.TryParse(host, out var ip) && IsBlockedIp(ip))
            throw new ArgumentException("Endpoint targets a blocked private/internal IP address.");
    }

    // ================================================================
    //  Layer 2: Async DNS resolution + IP range check
    // ================================================================

    /// <summary>
    /// Resolves the hostname via DNS and ensures no resolved IP falls in blocked ranges.
    /// Call this right before making the actual HTTP request (catches DNS rebinding).
    /// </summary>
    public static async Task EnsureSafeAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Endpoint must be a valid URL.");

        var host = uri.Host;

        // Re-run the hostname check (defense in depth)
        RejectBlockedHostname(url);

        // Resolve DNS
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            throw new ArgumentException($"Cannot resolve endpoint hostname: {host}");
        }

        if (addresses.Length == 0)
            throw new ArgumentException($"Endpoint hostname resolved to no addresses: {host}");

        foreach (var ip in addresses)
        {
            if (IsBlockedIp(ip))
            {
                throw new ArgumentException(
                    "Endpoint resolves to a blocked private/internal IP address.");
            }
        }
    }

    // ================================================================
    //  IP classification
    // ================================================================

    private static bool IsBlockedIp(IPAddress ip)
    {
        // Normalize IPv4-mapped IPv6 (e.g., ::ffff:127.0.0.1 → 127.0.0.1)
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;                              // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;              // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;              // 169.254.0.0/16 (link-local + cloud metadata)
            if (b[0] == 0) return true;                                // 0.0.0.0/8
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;                       // fe80::/10
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;                   // fc00::/7 (unique local)
            if (ip.Equals(IPAddress.IPv6None)) return true;            // ::
        }

        return false;
    }
}
