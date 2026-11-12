using NetPilot.Core.Models;

namespace NetPilot.Health;

// Describes only reviewed health-test capability; never a blanket claim that
// the corresponding desktop/mobile app is routed or UDP/QUIC is supported.
public enum ServiceCategory
{
    Communication,
    Video,
    Gaming,
    Work,
    Other
}

public enum ServiceConnectivity
{
    // Tests only fixed, public HTTPS endpoints through local ByeDPI SOCKS5.
    // Actual browser/application traffic is not routed by NetPilot today.
    HttpsOverLocalSocks5ProbeOnly
}

public sealed record ReviewedService(
    ResourceDefinition Resource,
    ServiceCategory Category,
    ServiceConnectivity Connectivity);
