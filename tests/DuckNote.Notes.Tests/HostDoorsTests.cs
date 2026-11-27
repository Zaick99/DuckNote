using DuckNote.Notes.Hosts;

namespace DuckNote.Notes.Tests;

public class HostDoorsTests
{
    [Fact]
    public void Web_ports_become_pages_the_secure_ones_first()
    {
        IReadOnlyList<HostDoor> doors = HostDoors.For("192.168.10.2", "22, 80, 443, 8006", mac: true);

        Assert.Equal(["https://192.168.10.2/", "http://192.168.10.2/", "https://192.168.10.2:8006/"],
            doors.Where(door => door.Kind == "web").Select(door => door.Target));
        Assert.Equal("https://192.168.10.2", doors[0].Label);
    }

    [Fact]
    public void On_the_Mac_shares_open_in_the_Finder_and_ssh_in_the_Terminal()
    {
        IReadOnlyList<HostDoor> doors = HostDoors.For("nas01.example.local", "22,139,445,5000", mac: true);

        Assert.Contains(doors, door => door.Kind == "share" && door.Target == "smb://nas01.example.local/");
        Assert.Contains(doors, door => door.Kind == "ssh" && door.Target == "ssh://nas01.example.local");
        Assert.Contains(doors, door => door.Kind == "web" && door.Target == "http://nas01.example.local:5000/");
        Assert.DoesNotContain(doors, door => door.Kind == "rdp");
    }

    [Fact]
    public void On_Windows_shares_open_in_Explorer_and_3389_is_remote_desktop()
    {
        IReadOnlyList<HostDoor> doors = HostDoors.For("192.168.10.101", "135, 445, 3389", mac: false);

        Assert.Equal(["share", "rdp"], doors.Select(door => door.Kind));
        Assert.Equal(@"\\192.168.10.101", doors[0].Target);
        Assert.Equal("192.168.10.101", doors[1].Target);
    }

    [Fact]
    public void Nothing_open_nothing_to_offer()
    {
        Assert.Empty(HostDoors.For("10.0.0.5", "", mac: true));
        Assert.Empty(HostDoors.For("", "80", mac: false));
        Assert.Empty(HostDoors.For("10.0.0.5", "53, 161", mac: true));
    }

    [Fact]
    public void An_ipv6_address_goes_in_brackets()
    {
        HostDoor web = Assert.Single(HostDoors.For("fe80::1", "443", mac: true));

        Assert.Equal("https://[fe80::1]/", web.Target);
    }
}
