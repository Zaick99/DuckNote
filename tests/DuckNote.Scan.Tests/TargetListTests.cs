using DuckNote.Scan;

namespace DuckNote.Scan.Tests;

public class TargetListTests
{
    [Fact]
    public void A_slash_24_drops_network_and_broadcast()
    {
        IReadOnlyList<string> targets = TargetList.Expand("192.168.1.0/24");

        Assert.Equal(254, targets.Count);
        Assert.Equal("192.168.1.1", targets[0]);
        Assert.Equal("192.168.1.254", targets[^1]);
    }

    [Fact]
    public void A_slash_32_is_the_single_host_itself()
    {
        Assert.Equal(["10.0.0.7"], TargetList.Expand("10.0.0.7/32"));
    }

    [Fact]
    public void A_slash_30_leaves_the_two_usable_addresses()
    {
        Assert.Equal(["10.0.0.1", "10.0.0.2"], TargetList.Expand("10.0.0.0/30"));
    }

    [Fact]
    public void A_full_range_is_walked_end_to_end()
    {
        IReadOnlyList<string> targets = TargetList.Expand("10.0.0.5-10.0.0.8");

        Assert.Equal(["10.0.0.5", "10.0.0.6", "10.0.0.7", "10.0.0.8"], targets);
    }

    [Fact]
    public void A_trailing_octet_continues_the_address_it_follows()
    {
        IReadOnlyList<string> targets = TargetList.Expand("192.168.1.250-254");

        Assert.Equal(
            ["192.168.1.250", "192.168.1.251", "192.168.1.252", "192.168.1.253", "192.168.1.254"],
            targets);
    }

    [Fact]
    public void Comma_separated_pieces_add_up_without_repeating()
    {
        IReadOnlyList<string> targets = TargetList.Expand("10.0.0.1, 10.0.0.1, nas.casa.lan, 10.0.0.2-10.0.0.3");

        Assert.Equal(["10.0.0.1", "nas.casa.lan", "10.0.0.2", "10.0.0.3"], targets);
    }

    [Fact]
    public void A_bare_name_passes_through_untouched()
    {
        Assert.Equal(["nas.casa.lan"], TargetList.Expand("nas.casa.lan"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_written_means_nothing_to_scan(string? expression)
    {
        Assert.Empty(TargetList.Expand(expression));
    }

    [Fact]
    public void A_backwards_range_is_left_alone_instead_of_exploding()
    {
        Assert.Equal(["10.0.0.9-10.0.0.1"], TargetList.Expand("10.0.0.9-10.0.0.1"));
    }

    [Fact]
    public void An_oversized_range_is_refused_with_advice()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => TargetList.Expand("10.0.0.0/8"));

        Assert.Contains("restringilo", error.Message, StringComparison.Ordinal);
    }
}
