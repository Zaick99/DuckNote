using DuckNote.Scan;

namespace DuckNote.Scan.Tests;

public class FingerprintTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(20, "Embedded / legacy")]
    [InlineData(64, "Linux / Unix / macOS")]
    [InlineData(57, "Linux / Unix / macOS")]
    [InlineData(128, "Windows")]
    [InlineData(115, "Windows")]
    [InlineData(255, "Apparato di rete (Cisco/Solaris)")]
    public void The_ttl_suggests_the_system_that_answered(int ttl, string expected)
    {
        Assert.Equal(expected, TtlFingerprint.GuessOperatingSystem(ttl));
    }

    [Theory]
    [InlineData(64, 0)]
    [InlineData(57, 7)]
    [InlineData(128, 0)]
    [InlineData(120, 8)]
    [InlineData(0, 0)]
    public void The_distance_is_the_gap_from_the_typical_starting_value(int ttl, int hops)
    {
        Assert.Equal(hops, TtlFingerprint.CountHops(ttl));
    }

    [Fact]
    public void A_known_port_is_named_and_an_unknown_one_is_not_invented()
    {
        Assert.Equal("https", ServiceNames.Describe(443));
        Assert.Equal("proxmox", ServiceNames.Describe(8006));
        Assert.Equal("tcp/57221", ServiceNames.Describe(57221));
    }

    [Fact]
    public void Services_are_listed_port_first()
    {
        Assert.Equal("22/ssh, 443/https", ServiceNames.DescribeAll([22, 443]));
    }

    [Theory]
    [InlineData(9100, "Stampante")]
    [InlineData(8006, "Proxmox VE")]
    [InlineData(32400, "Media server")]
    [InlineData(2049, "NAS")]
    [InlineData(3389, "Host Windows")]
    [InlineData(5060, "VoIP")]
    public void An_open_port_can_be_enough_to_name_the_device(int port, string expected)
    {
        Assert.Equal(expected, DeviceTypes.Classify([port], "", "", "", "", ""));
    }

    [Fact]
    public void A_banner_names_the_device_when_the_ports_do_not()
    {
        Assert.Equal("Router / Gateway", DeviceTypes.Classify([], "MikroTik", "", "", "", ""));
        Assert.Equal("Dispositivo IoT", DeviceTypes.Classify([], "", "", "", "Shelly Plug S", ""));
    }

    [Fact]
    public void Nothing_open_and_nothing_said_names_nothing()
    {
        Assert.Equal("", DeviceTypes.Classify([], "", "", "", "", ""));
    }

    [Fact]
    public void Exposed_legacy_services_are_called_out()
    {
        string findings = SecurityFindings.Describe([21, 23, 139, 445, 3389], tlsExpiry: "");

        Assert.Equal("Telnet aperto - FTP aperto - RDP esposto - SMB legacy", findings);
    }

    [Fact]
    public void An_expired_certificate_is_a_finding_and_a_valid_one_is_not()
    {
        Assert.Contains("Certificato TLS scaduto",
            SecurityFindings.Describe([443], tlsExpiry: "2020-01-01"), StringComparison.Ordinal);

        Assert.Equal("", SecurityFindings.Describe([443], tlsExpiry: "2099-01-01"));
    }
}
