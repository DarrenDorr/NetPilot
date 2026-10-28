using System.Net;
using System.Net.Sockets;

namespace NetPilot.Health;

public static class PublicEndpointPolicy
{
    public static void ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new HealthCheckSecurityException(
                "HttpsRequired",
                "Health-check разрешает только абсолютные HTTPS-адреса.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new HealthCheckSecurityException(
                "UserInfoRejected",
                "Учётные данные в health-check URL запрещены.");
        }

        if (uri.Port != 443)
        {
            throw new HealthCheckSecurityException(
                "NonStandardPortRejected",
                "В первой версии health-check разрешён только HTTPS-порт 443.");
        }

        var host = uri.IdnHost;
        if (string.IsNullOrWhiteSpace(host)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new HealthCheckSecurityException(
                "LocalHostRejected",
                "Локальные адреса запрещены для health-check.");
        }

        if (IPAddress.TryParse(host, out var literalAddress))
        {
            EnsurePublicAddress(literalAddress);
        }
    }

    public static void EnsureAllResolvedAddressesArePublic(IReadOnlyCollection<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Count == 0)
        {
            throw new HealthCheckSecurityException(
                "DnsNoAddresses",
                "DNS не вернул адресов для health-check endpoint.");
        }

        foreach (var address in addresses)
        {
            EnsurePublicAddress(address);
        }
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIpv4(address),
            AddressFamily.InterNetworkV6 => IsPublicIpv6(address),
            _ => false
        };
    }

    private static void EnsurePublicAddress(IPAddress address)
    {
        if (!IsPublicAddress(address))
        {
            throw new HealthCheckSecurityException(
                "NonPublicAddressRejected",
                "Health-check endpoint разрешён только для публичных сетевых адресов.");
        }
    }

    private static bool IsPublicIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];
        var third = bytes[2];

        if (first == 0
            || first == 10
            || first == 127
            || first >= 224)
        {
            return false;
        }

        if (first == 100 && second is >= 64 and <= 127)
        {
            return false;
        }

        if (first == 169 && second == 254)
        {
            return false;
        }

        if (first == 172 && second is >= 16 and <= 31)
        {
            return false;
        }

        if (first == 192 && second == 168)
        {
            return false;
        }

        if (first == 192 && second == 0 && third == 0)
        {
            return false;
        }

        if (first == 192 && second == 0 && third == 2)
        {
            return false;
        }

        if (first == 192 && second == 88 && third == 99)
        {
            return false;
        }

        if (first == 198 && second is 18 or 19)
        {
            return false;
        }

        if (first == 198 && second == 51 && third == 100)
        {
            return false;
        }

        if (first == 203 && second == 0 && third == 113)
        {
            return false;
        }

        return true;
    }

    private static bool IsPublicIpv6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // fc00::/7 — Unique Local Address.
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return false;
        }

        // 2001:db8::/32 — документационный диапазон.
        if (bytes[0] == 0x20
            && bytes[1] == 0x01
            && bytes[2] == 0x0D
            && bytes[3] == 0xB8)
        {
            return false;
        }

        // 100::/64 — discard-only prefix.
        if (bytes[0] == 0x01
            && bytes[1] == 0x00
            && bytes.Skip(2).Take(6).All(static value => value == 0))
        {
            return false;
        }

        return true;
    }
}
