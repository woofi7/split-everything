using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SplitEverything.Api.Infrastructure;

/// <summary>One IPv4 address the machine holds, with what matters for choosing it.</summary>
public sealed record NetworkCandidate(string Address, bool HasGateway, bool IsLoopback, bool IsWireless);

/// <summary>
/// The address on this machine that another device on the same network can reach.
///
/// A development box has many: loopback, a real LAN interface, a Docker bridge per
/// compose project, VPN adapters. Only one of them is useful in a link handed to a
/// phone, and picking wrong is invisible until someone scans a QR code and gets
/// nothing.
/// </summary>
public static class LocalNetworkAddress
{
    /// <summary>
    /// The policy, kept pure so it can be tested without asking the machine
    /// anything. A gateway is what separates a real network from a container
    /// bridge, and wireless comes first because a laptop testing against a phone
    /// is usually on wifi.
    /// </summary>
    public static string? Choose(IReadOnlyCollection<NetworkCandidate> candidates)
        => candidates
            .Where(candidate => !candidate.IsLoopback)
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => candidate.IsWireless)
            .FirstOrDefault()
            ?.Address;

    /// <summary>Asks the machine, then applies the policy. Null if nothing qualifies.</summary>
    public static string? Detect()
    {
        try
        {
            return Choose(Candidates());
        }
        catch (NetworkInformationException)
        {
            // Not knowing is a normal answer here: the caller leaves the configured
            // value alone rather than guessing.
            return null;
        }
    }

    private static List<NetworkCandidate> Candidates()
    {
        var candidates = new List<NetworkCandidate>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;

            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses
                .Any(gateway => gateway.Address is { } address
                    && address.AddressFamily == AddressFamily.InterNetwork
                    && !address.Equals(IPAddress.Any));

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                candidates.Add(new NetworkCandidate(
                    unicast.Address.ToString(),
                    hasGateway,
                    IPAddress.IsLoopback(unicast.Address),
                    nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
            }
        }

        return candidates;
    }
}

/// <summary>
/// The invite base URL, adjusted for a development machine.
///
/// Invite links and their QR codes are built from Auth:AppBaseUrl, which defaults
/// to localhost. On a phone that address means the phone, so every link scanned
/// from a development box was a dead end.
/// </summary>
public static class DevelopmentAppBaseUrl
{
    /// <summary>
    /// Swaps a loopback host for one another device can reach, keeping the scheme,
    /// port and path.
    ///
    /// Only loopback: that is the default nobody chose, and the only host that is
    /// actively wrong for another device. A host someone set deliberately is left
    /// alone, and so is anything unparseable, since a startup rewrite is no place
    /// to reject configuration.
    /// </summary>
    public static string Rewrite(string configured, string? localAddress)
    {
        if (string.IsNullOrWhiteSpace(configured) || localAddress is null) return configured;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)) return configured;
        if (!IsLoopbackHost(uri)) return configured;

        return new UriBuilder(uri) { Host = localAddress }.Uri.ToString().TrimEnd('/');
    }

    private static bool IsLoopbackHost(Uri uri)
        => uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
