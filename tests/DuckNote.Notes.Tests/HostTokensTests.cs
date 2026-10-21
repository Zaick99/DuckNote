using DuckNote.Notes.Hosts;

namespace DuckNote.Notes.Tests;

public class HostTokensTests
{
    private static string[] Values(string text) => [.. HostTokens.Find(text).Select(token => token.Value)];

    [Fact]
    public void Addresses_are_found_in_the_middle_of_a_sentence()
    {
        Assert.Equal(
            ["192.168.1.1", "nas.home.lan"],
            Values("The gateway is 192.168.1.1, and behind it the NAS nas.home.lan that keeps vanishing."));
    }

    [Fact]
    public void Ipv6_and_ipv4_on_the_same_line_are_all_found()
    {
        Assert.Equal(
            ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111"],
            Values("DNS: 1.1.1.1 and 1.0.0.1, plus 2606:4700:4700::1111 for IPv6."));
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("schema.sql")]
    [InlineData(@"C:\path\file.xaml")]
    [InlineData("version 1.2")]
    [InlineData("name@example.com")]
    [InlineData("Welcome.Net")]
    public void What_is_not_an_address_stays_out(string text) => Assert.Empty(Values(text));

    [Fact]
    public void A_prefix_belongs_to_the_address() => Assert.Equal(["10.0.0.0/8"], Values("rete 10.0.0.0/8 interna"));

    [Fact]
    public void A_version_with_four_parts_after_more_digits_is_not_an_address() =>
        Assert.Empty(Values("build 1.2.3.4.5"));

    [Fact]
    public void Mixed_ipv4_inside_ipv6_is_one_address() =>
        Assert.Equal(["::ffff:10.0.0.1"], Values("mappato ::ffff:10.0.0.1 qui"));

    [Fact]
    public void A_word_being_typed_is_touched_only_from_inside_or_right_after()
    {
        HostToken token = HostTokens.Find("vai su nb.zaick.net ora")[0];

        Assert.False(HostTokens.Touches(token, token.Start));
        Assert.True(HostTokens.Touches(token, token.Start + 1));
        Assert.True(HostTokens.Touches(token, token.End));
        Assert.False(HostTokens.Touches(token, token.End + 1));
    }

    [Fact]
    public void Only_accepts_a_lone_address()
    {
        Assert.Equal("10.0.0.5", HostTokens.Only("  10.0.0.5 "));
        Assert.Null(HostTokens.Only("ping 10.0.0.5"));
    }

    [Fact]
    public void At_finds_the_address_under_a_column()
    {
        Assert.Equal("nas.home.lan", HostTokens.At("il nas.home.lan e basta", 6));
        Assert.Null(HostTokens.At("il nas.home.lan e basta", 0));
    }

    [Fact]
    public void Ignored_hosts_round_trip_without_duplicates()
    {
        IReadOnlyList<string> hosts = HostList.Parse("10.0.0.1, nas.lan;10.0.0.1\nNAS.lan");

        Assert.Equal(["10.0.0.1", "nas.lan"], hosts);
        Assert.Equal("10.0.0.1,nas.lan", HostList.Write(hosts));
    }
}
