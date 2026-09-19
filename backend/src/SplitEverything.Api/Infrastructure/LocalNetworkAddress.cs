using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SplitEverything.Api.Infrastructure;

public sealed record NetworkCandidate(string Address, bool HasGateway, bool IsLoopback, bool IsWireless);

public static class LocalNetworkAddress
{
    public static string? Choose(IReadOnlyCollection<NetworkCandidate> candidates)
        => candidates
            .Where(candidate => !candidate.IsLoopback)
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => candidate.IsWireless)
            .FirstOrDefault()
            ?.Address;

    public static string? Detect()
    {
        try
        {
            return Choose(Candidates());
        }
        catch (NetworkInformationException)
        {
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

public static class DevelopmentAppBaseUrl
{
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
