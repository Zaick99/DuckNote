using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DuckNote.Scan.Probes;

public static class LocalNetwork
{
    private static readonly Lazy<IReadOnlyList<(uint Network, uint Mask)>> Segments = new(Discover);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> OwnAddresses = new(DiscoverOwn);

    public static string OwnMac(string address) =>
        OwnAddresses.Value.TryGetValue(address, out string? mac) ? mac : string.Empty;

    public static bool IsOnLink(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        uint value = ToUInt32(address);
        foreach ((uint network, uint mask) in Segments.Value)
        {
            if ((value & mask) == network)
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<(uint, uint)> Discover()
    {
        List<(uint, uint)> segments = [];

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                uint mask = ToUInt32(unicast.IPv4Mask);
                if (mask == 0)
                {
                    continue;
                }
                segments.Add((ToUInt32(unicast.Address) & mask, mask));
            }
        }

        return segments;
    }

    private static IReadOnlyDictionary<string, string> DiscoverOwn()
    {
        Dictionary<string, string> own = [];

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            byte[] hardware = adapter.GetPhysicalAddress().GetAddressBytes();
            if (hardware.Length != 6)
            {
                continue;
            }

            string mac = string.Join(':', hardware.Select(b => b.ToString("X2")));
            foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    own[unicast.Address.ToString()] = mac;
                }
            }
        }

        return own;
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] octets = address.GetAddressBytes();
        return ((uint)octets[0] << 24) | ((uint)octets[1] << 16) | ((uint)octets[2] << 8) | octets[3];
    }
}
