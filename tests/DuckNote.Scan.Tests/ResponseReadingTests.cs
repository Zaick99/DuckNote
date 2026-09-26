using DuckNote.Scan;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

public class ResponseReadingTests
{
    private const string ProxmoxResponse =
        "HTTP/1.1 200 OK\r\n" +
        "Server: pve-api-daemon/3.0\r\n" +
        "X-Powered-By: Proxmox\r\n" +
        "Content-Type: text/html\r\n\r\n" +
        "<html><head><title>Proxmox Virtual Environment</title></head><body>ciao</body></html>";

    [Fact]
    public void A_web_response_gives_code_server_and_title()
    {
        HttpFindings found = HttpResponseReader.Read(ProxmoxResponse);

        Assert.Equal("200", found.Code);
        Assert.Equal("pve-api-daemon/3.0", found.Server);
        Assert.Equal("Proxmox", found.PoweredBy);
        Assert.Equal("Proxmox Virtual Environment", found.Title);
        Assert.Equal("pve-api-daemon/3.0 / Proxmox", found.DescribeServer());
    }

    [Fact]
    public void A_redirect_is_kept_when_there_is_no_title()
    {
        HttpFindings found = HttpResponseReader.Read(
            "HTTP/1.1 302 Found\r\nLocation: https://nas.casa.lan/login\r\n\r\n");

        Assert.Equal("302", found.Code);
        Assert.Equal("https://nas.casa.lan/login", found.Redirect);
        Assert.Equal("", found.Title);
    }

    [Fact]
    public void A_password_prompt_becomes_the_title_when_there_is_no_page()
    {
        HttpFindings found = HttpResponseReader.Read(
            "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"Router Admin\"\r\n\r\n");

        Assert.Equal("realm: Router Admin", found.Title);
    }

    [Fact]
    public void Markup_inside_the_title_is_stripped()
    {
        HttpFindings found = HttpResponseReader.Read(
            "HTTP/1.1 200 OK\r\n\r\n<title>NAS <b>DS920+</b> admin</title>");

        Assert.Equal("NAS DS920+ admin", found.Title);
    }

    [Fact]
    public void Nothing_received_reads_as_nothing_found()
    {
        Assert.Equal(HttpFindings.Empty, HttpResponseReader.Read(""));
    }

    [Fact]
    public void An_SSDP_reply_gives_server_location_and_target()
    {
        SsdpFindings found = SsdpProbe.ReadReply(
            "HTTP/1.1 200 OK\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            "LOCATION: http://192.168.1.1:5000/rootDesc.xml\r\n" +
            "SERVER: Linux/5.15 UPnP/1.0 MiniUPnPd/2.3\r\n" +
            "ST: upnp:rootdevice\r\n\r\n");

        Assert.Equal("Linux/5.15 UPnP/1.0 MiniUPnPd/2.3", found.Server);
        Assert.Equal("http://192.168.1.1:5000/rootDesc.xml", found.Location);
        Assert.Equal("upnp:rootdevice", found.SearchTarget);
    }

    [Fact]
    public void A_device_description_names_the_thing()
    {
        string device = SsdpProbe.ReadDescription(
            "<root><device><friendlyName>FRITZ!Box 7590</friendlyName>" +
            "<manufacturer>AVM Berlin</manufacturer><modelName>FRITZ!Box</modelName></device></root>");

        Assert.Equal("FRITZ!Box 7590 / AVM Berlin / FRITZ!Box", device);
    }

    [Fact]
    public void A_description_missing_its_fields_yields_nothing()
    {
        Assert.Equal("", SsdpProbe.ReadDescription("<root><device/></root>"));
    }

    [Theory]
    [InlineData("Documenti     Disk      Cartella condivisa", "Documenti")]
    [InlineData("Stampante     Print     HP LaserJet", "Stampante")]
    [InlineData("Archivio      Disco     Note", "Archivio")]
    public void Share_rows_are_read_in_both_languages(string row, string expected)
    {
        Assert.Equal([expected], SharesProbe.ReadShares(row));
    }

    [Fact]
    public void Headers_and_blank_lines_are_not_shares()
    {
        IReadOnlyList<string> shares = SharesProbe.ReadShares(
            "Shared resources at \\\\192.168.1.10\r\n\r\n" +
            "Share name   Type   Used as  Comment\r\n" +
            "-------------------------------------\r\n" +
            "Media        Disk\r\n" +
            "Backup       Disk\r\n" +
            "The command completed successfully.\r\n");

        Assert.Equal(["Media", "Backup"], shares);
    }

    [Fact]
    public void A_refined_guess_replaces_a_family_with_a_name()
    {
        Assert.Equal(
            "Linux gw 5.15.0 #1 SMP x86_64",
            OperatingSystemGuess.Refine("Linux / Unix / macOS", "Linux gw 5.15.0 #1 SMP x86_64", ""));
    }

    [Fact]
    public void A_windows_guess_is_not_overwritten_by_an_SNMP_description()
    {
        Assert.Equal("Windows", OperatingSystemGuess.Refine("Windows", "Linux something", ""));
    }

    [Fact]
    public void An_ssh_banner_names_the_distribution_when_nothing_else_did()
    {
        Assert.Equal(
            "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.4",
            OperatingSystemGuess.Refine("", "", "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.4"));
    }
}
