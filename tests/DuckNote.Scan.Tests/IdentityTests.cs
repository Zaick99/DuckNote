using System.Text;
using DuckNote.Scan;
using DuckNote.Scan.Probes;
using DuckNote.Scan.Wire;

namespace DuckNote.Scan.Tests;

/// <summary>Il riconoscimento degli apparati: telefoni, Mac, PC, server, NAS e apparati di casa.</summary>
public class IdentityTests
{
    private static DeviceIdentityResult Identify(DeviceSignals signals) => DeviceIdentity.Identify(signals);

    [Fact]
    public void An_iphone_with_a_private_address_is_an_iphone_not_windows()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 64,
            Mac = "5A:1C:3E:22:91:04",
            Vendor = "MAC locale / randomizzato",
            OpenPorts = [62078],
            MdnsName = "iPhone-di-Marta.local",
        });

        Assert.Equal("iOS", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.IPhone, found.DeviceType);
        Assert.Contains("62078", found.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void An_android_phone_is_recognised_from_the_maker_of_its_card()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, Mac = "4C:49:E3:10:20:30", Vendor = "Xiaomi" });

        Assert.Equal("Android", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.AndroidPhone, found.DeviceType);
    }

    [Fact]
    public void A_galaxy_with_a_private_address_is_android()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 64,
            Mac = "AE:11:22:33:44:55",
            Vendor = "MAC locale / randomizzato",
            Hostname = "Galaxy-S23.lan",
        });

        Assert.Equal("Android", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.AndroidPhone, found.DeviceType);
    }

    [Fact]
    public void A_mac_says_its_model_and_version_over_mdns()
    {
        MdnsServices mdns = MdnsServices.Read(MdnsReply(
            ("_device-info._tcp.local", "MacBook di Marta._device-info._tcp.local"),
            txt: ("MacBook di Marta._device-info._tcp.local", ["model=MacBookPro18,3", "osxvers=23"])));

        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 64,
            Mac = "4C:7C:D9:01:02:03",
            Vendor = "Apple",
            OpenPorts = [22, 88, 445],
            Mdns = mdns,
        });

        Assert.Equal("macOS 14 Sonoma", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.Mac, found.DeviceType);
        Assert.Equal("MacBook Pro (MacBookPro18,3)", found.Model);
    }

    [Fact]
    public void A_windows_pc_is_still_windows()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 128,
            Vendor = "Intel",
            OpenPorts = [135, 139, 445],
            NetBiosName = "DESKTOP-4F2K9QX",
        });

        Assert.Equal("Windows", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.WindowsPc, found.DeviceType);
    }

    [Fact]
    public void A_linux_server_says_its_distribution_over_ssh()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 64,
            Vendor = "Intel",
            OpenPorts = [22, 80],
            SshBanner = "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.4",
        });

        Assert.Equal("Ubuntu", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.LinuxHost, found.DeviceType);
    }

    [Fact]
    public void A_nas_sharing_files_with_samba_is_not_windows()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals
        {
            Ttl = 64,
            Vendor = "Synology",
            OpenPorts = [22, 139, 445, 5000, 5001],
            NetBiosName = "DISKSTATION",
            HttpTitle = "Synology DiskStation",
        });

        Assert.Equal("Synology DSM", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.Nas, found.DeviceType);
    }

    [Fact]
    public void Samba_or_xrdp_alone_on_a_ttl_of_64_is_not_windows()
    {
        Assert.DoesNotContain("Windows", Identify(new DeviceSignals { Ttl = 64, OpenPorts = [445] }).OperatingSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows", Identify(new DeviceSignals { Ttl = 64, OpenPorts = [3389] }).DeviceType, StringComparison.Ordinal);
    }

    [Fact]
    public void A_raspberry_pi_is_recognised()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, Mac = "D8:3A:DD:00:11:22", Vendor = "Raspberry Pi", OpenPorts = [22] });

        Assert.Equal("Raspberry Pi OS", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.RaspberryPi, found.DeviceType);
    }

    [Fact]
    public void A_chromecast_is_a_tv_device()
    {
        MdnsServices mdns = MdnsServices.Read(MdnsReply(
            ("_googlecast._tcp.local", "Chromecast-1234._googlecast._tcp.local"),
            txt: ("Chromecast-1234._googlecast._tcp.local", ["md=Chromecast", "fn=TV del salotto"])));

        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, Vendor = "Google", OpenPorts = [8008, 8009], Mdns = mdns });

        Assert.Equal(DeviceIdentity.TvCast, found.DeviceType);
        Assert.Equal("Android TV / Google Cast", found.OperatingSystem);
        Assert.Equal("Chromecast", found.Model);
    }

    [Fact]
    public void A_printer_says_its_model()
    {
        MdnsServices mdns = MdnsServices.Read(MdnsReply(
            ("_ipp._tcp.local", "HP LaserJet._ipp._tcp.local"),
            txt: ("HP LaserJet._ipp._tcp.local", ["ty=HP LaserJet Pro M404dn", "product=(HP LaserJet Pro M404dn)"])));

        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, OpenPorts = [80, 443, 631, 9100], Mdns = mdns });

        Assert.Equal(DeviceIdentity.Printer, found.DeviceType);
        Assert.Equal("HP LaserJet Pro M404dn", found.Model);
    }

    [Fact]
    public void A_mikrotik_is_a_router_with_routeros()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, Vendor = "MikroTik", OpenPorts = [22, 53, 80, 8291] });

        Assert.Equal("RouterOS", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.Router, found.DeviceType);
    }

    [Theory]
    [InlineData(64, "Linux / Unix / macOS")]
    [InlineData(128, "Windows")]
    [InlineData(255, "Apparato di rete (Cisco/Solaris)")]
    public void With_only_the_ttl_only_the_family_is_said(int ttl, string expected)
    {
        Assert.Equal(expected, Identify(new DeviceSignals { Ttl = ttl }).OperatingSystem);
    }

    [Fact]
    public void A_private_address_and_a_ttl_of_64_is_a_phone_of_some_kind()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 64, Mac = "62:11:22:33:44:55", Vendor = "MAC locale / randomizzato" });

        Assert.Equal("iOS o Android", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.Mobile, found.DeviceType);
    }

    [Fact]
    public void Wmi_is_the_final_word()
    {
        DeviceIdentityResult found = Identify(new DeviceSignals { Ttl = 128, OpenPorts = [135, 445, 5985], WmiOs = "Microsoft Windows 11 Pro", WmiModel = "OptiPlex 7010" });

        Assert.Equal("Microsoft Windows 11 Pro", found.OperatingSystem);
        Assert.Equal(DeviceIdentity.WindowsPc, found.DeviceType);
        Assert.Equal("OptiPlex 7010", found.Model);
    }

    [Fact]
    public void The_full_ieee_registry_knows_the_makers_the_short_list_did_not()
    {
        OuiVendorLookup vendors = new();

        Assert.Equal("Apple", vendors.Describe("4C:7C:D9:00:00:01"));
        Assert.Equal("Xiaomi", vendors.Describe("4C:49:E3:00:00:01"));
        // l'elenco scritto a mano vince: i suoi nomi dicono il tipo
        Assert.Equal("Raspberry Pi", vendors.Describe("B8:27:EB:00:00:01"));
        Assert.True(vendors.Count > 40_000, $"solo {vendors.Count} prefissi");
    }

    [Fact]
    public void The_real_ttl_is_read_from_the_ip_header_of_our_echo_reply()
    {
        byte[] marker = Encoding.ASCII.GetBytes("DuckNote-marker!");
        byte[] icmp = IcmpTtl.EchoPacket(7, 9, marker);
        icmp[0] = 0; // risposta all'echo

        byte[] withHeader = [0x45, 0, 0, 44, 0, 0, 0, 0, 57, 1, 0, 0, 192, 168, 1, 20, 192, 168, 1, 2, .. icmp];
        Assert.Equal(57, IcmpTtl.TtlOf(withHeader, marker));

        // senza intestazione (socket datagramma su Linux): e' la nostra, ma il TTL non si sa
        Assert.Equal(0, IcmpTtl.TtlOf(icmp, marker));

        // la risposta a qualcun altro non conta
        Assert.Equal(-1, IcmpTtl.TtlOf(withHeader, Encoding.ASCII.GetBytes("un altro marker!")));
    }

    [Fact]
    public void The_echo_request_has_a_valid_checksum()
    {
        byte[] packet = IcmpTtl.EchoPacket(0x1234, 1, Encoding.ASCII.GetBytes("abc"));
        uint sum = 0;
        for (int i = 0; i < packet.Length; i += 2)
        {
            sum += (uint)((packet[i] << 8) | (i + 1 < packet.Length ? packet[i + 1] : 0));
        }
        while (sum >> 16 != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        Assert.Equal(8, packet[0]);
        Assert.Equal(0xFFFFu, sum);
    }

    [Fact]
    public void Mdns_answers_give_the_services_and_their_values()
    {
        MdnsServices found = MdnsServices.Read(MdnsReply(
            ("_airplay._tcp.local", "Salotto._airplay._tcp.local"),
            txt: ("Salotto._airplay._tcp.local", ["model=AppleTV14,1", "deviceid=AA:BB"])));

        Assert.True(found.Has("airplay"));
        Assert.Equal("AppleTV14,1", found.Value("model"));
        Assert.Equal("", found.Value("deviceid"));
        Assert.True(found.HasTxt("Salotto._airplay._tcp.local"));

        DeviceIdentityResult tv = Identify(new DeviceSignals { Ttl = 64, Vendor = "Apple", OpenPorts = [7000], Mdns = found });
        Assert.Equal(DeviceIdentity.AppleTv, tv.DeviceType);
        Assert.Equal("tvOS", tv.OperatingSystem);
    }

    [Fact]
    public void The_mdns_question_asks_every_service_and_wants_a_direct_answer()
    {
        byte[] query = DnsMessage.BuildMdnsQuery([("_airplay._tcp.local", DnsMessage.TypePtr), ("_ipp._tcp.local", DnsMessage.TypePtr)], 0x0102);

        Assert.Equal(2, (query[4] << 8) | query[5]);
        // classe IN col bit QU (risposta diretta a chi chiede)
        int end = 12 + DnsMessage.EncodeLabels("_airplay._tcp.local").Length;
        Assert.Equal(0x80, query[end + 2]);
        Assert.Equal(0x01, query[end + 3]);
    }

    /// <summary>Una risposta mDNS con un PTR (servizio -> istanza) e il TXT dell'istanza tra gli aggiuntivi.</summary>
    private static byte[] MdnsReply((string Service, string Instance) ptr, (string Instance, string[] Strings) txt)
    {
        List<byte> packet = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 1];
        byte[] target = DnsMessage.EncodeLabels(ptr.Instance);
        packet.AddRange(DnsMessage.EncodeLabels(ptr.Service));
        packet.AddRange([0, 12, 0, 1, 0, 0, 0, 10, (byte)(target.Length >> 8), (byte)target.Length]);
        packet.AddRange(target);

        List<byte> data = [];
        foreach (string text in txt.Strings)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            data.Add((byte)bytes.Length);
            data.AddRange(bytes);
        }
        packet.AddRange(DnsMessage.EncodeLabels(txt.Instance));
        packet.AddRange([0, 16, 0x80, 1, 0, 0, 0, 10, (byte)(data.Count >> 8), (byte)data.Count]);
        packet.AddRange(data);
        return [.. packet];
    }
}
