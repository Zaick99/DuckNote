using System.Text;
using System.Text.Json;
using DuckNote.Scan;
using DuckNote.Scan.Wire;

namespace DuckNote.Scan.Tests;

public class WireCompatibilityTests
{
    private static readonly JsonElement Expected = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wire-powershell.json"))).RootElement;

    private static byte[] Bytes(string base64) => Convert.FromBase64String(base64);

    private static IEnumerable<(string Key, JsonElement Value)> Entries(string section) =>
        Expected.GetProperty(section).EnumerateObject().Select(p => (p.Name, p.Value));

    [Fact]
    public void NetBios_names_are_encoded_exactly_as_PowerShell_encoded_them()
    {
        foreach ((string name, JsonElement encoded) in Entries("NetBiosEncode"))
        {
            Assert.Equal(Bytes(encoded.GetString()!), NetBiosMessage.EncodeName(name));
        }
    }

    [Fact]
    public void Dns_labels_are_encoded_exactly_as_PowerShell_encoded_them()
    {
        foreach ((string name, JsonElement encoded) in Entries("DnsLabels"))
        {
            Assert.Equal(Bytes(encoded.GetString()!), DnsMessage.EncodeLabels(name));
        }
    }

    [Fact]
    public void Reverse_lookup_names_match()
    {
        foreach ((string address, JsonElement arpa) in Entries("ArpaName"))
        {
            Assert.Equal(arpa.GetString(), DnsMessage.ArpaName(address));
        }
    }

    [Fact]
    public void Ber_lengths_are_encoded_identically_across_the_short_and_long_forms()
    {
        foreach ((string length, JsonElement encoded) in Entries("BerLen"))
        {
            Assert.Equal(Bytes(encoded.GetString()!), BerWriter.EncodeLength(int.Parse(length)));
        }
    }

    [Fact]
    public void Object_identifiers_are_encoded_identically_including_multi_byte_arcs()
    {
        foreach ((string oid, JsonElement encoded) in Entries("BerOid"))
        {
            int[] arcs = [.. oid.Split('.').Select(int.Parse)];
            Assert.Equal(Bytes(encoded.GetString()!), BerWriter.EncodeOid(arcs));
        }
    }

    [Fact]
    public void A_whole_SNMP_get_is_byte_for_byte_the_packet_PowerShell_sent()
    {
        byte[] expected = Bytes(Expected.GetProperty("SnmpGet").GetString()!);

        byte[] actual = SnmpMessage.BuildGet("public", SnmpMessage.SystemGroup, requestId: 123456);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void An_SNMP_reply_is_read_into_the_same_values()
    {
        byte[] reply = Bytes(Expected.GetProperty("SnmpReply").GetString()!);
        JsonElement parsed = Expected.GetProperty("SnmpParsed");

        IReadOnlyDictionary<string, string> actual = SnmpMessage.ReadResponse(reply, SnmpMessage.SystemGroup);

        Assert.Equal(parsed.GetProperty("Descr").GetString(), actual["Description"]);
        foreach (string field in (string[])["ObjectId", "Uptime", "Contact", "Name", "Location"])
        {
            Assert.Equal(parsed.GetProperty(field).GetString(), actual[field]);
        }
    }

    [Fact]
    public void Uptime_is_rendered_the_same_way()
    {
        byte[] reply = Bytes(Expected.GetProperty("SnmpReply").GetString()!);

        IReadOnlyDictionary<string, string> actual = SnmpMessage.ReadResponse(reply, SnmpMessage.SystemGroup);

        Assert.Equal(Expected.GetProperty("SnmpParsed").GetProperty("Uptime").GetString(), actual["Uptime"]);
    }

    [Fact]
    public void A_PTR_reply_with_a_compression_pointer_decodes_to_the_same_name()
    {
        byte[] packet = Bytes(Expected.GetProperty("DnsPtrPacket").GetString()!);

        Assert.Equal(
            Expected.GetProperty("DnsPtrAnswer").GetString(),
            DnsMessage.ReadAnswer(packet, DnsMessage.TypePtr));
    }

    [Fact]
    public void An_A_reply_decodes_to_the_same_address()
    {
        byte[] packet = Bytes(Expected.GetProperty("DnsAPacket").GetString()!);

        Assert.Equal(
            Expected.GetProperty("DnsAAnswer").GetString(),
            DnsMessage.ReadAnswer(packet, DnsMessage.TypeA));
    }

    [Fact]
    public void Dirty_text_is_cleaned_the_same_way()
    {
        foreach (JsonElement sample in Expected.GetProperty("Clean").EnumerateArray())
        {
            string input = Encoding.UTF8.GetString(Bytes(sample.GetProperty("In").GetString()!));

            Assert.Equal(sample.GetProperty("Out").GetString() ?? string.Empty, TextSanitiser.Clean(input));
        }
    }

    [Fact]
    public void The_built_in_vendor_table_holds_the_same_number_of_prefixes()
    {
        OuiVendorLookup lookup = new();

        Assert.Equal(Expected.GetProperty("OuiCount").GetInt32(), lookup.Count);
    }

    [Fact]
    public void Vendors_are_read_from_a_MAC_the_same_way()
    {
        OuiVendorLookup lookup = new();

        foreach ((string mac, JsonElement vendor) in Entries("VendorFromMac"))
        {
            Assert.Equal(vendor.GetString() ?? string.Empty, lookup.Describe(mac));
        }
    }

    [Fact]
    public void Vendors_are_guessed_from_text_the_same_way()
    {
        foreach ((string text, JsonElement vendor) in Entries("VendorFromText"))
        {
            Assert.Equal(vendor.GetString() ?? string.Empty, VendorFromText.Identify(text));
        }
    }
}
