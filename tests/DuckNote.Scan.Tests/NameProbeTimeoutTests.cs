using System.Diagnostics;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

public class NameProbeTimeoutTests
{
    private const string NoReverseRecord = "192.0.2.77";

    private static readonly TimeSpan Declared = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan Tolerated = TimeSpan.FromSeconds(4);

    [Fact]
    public async Task A_reverse_lookup_that_never_answers_gives_up_on_time()
    {
        Stopwatch clock = Stopwatch.StartNew();

        string name = await NameProbe.ReverseAsync(NoReverseRecord, dnsServer: "", Declared);

        clock.Stop();
        Assert.Equal(string.Empty, name);
        Assert.True(clock.Elapsed < Tolerated,
            $"Il limite dichiarato era {Declared.TotalMilliseconds} ms, ne sono passati {clock.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task A_name_that_does_not_exist_gives_up_on_time()
    {
        Stopwatch clock = Stopwatch.StartNew();

        string address = await NameProbe.ResolveAsync(
            "questo-nome-non-esiste.invalid", dnsServer: "", Declared);

        clock.Stop();
        Assert.Equal(string.Empty, address);
        Assert.True(clock.Elapsed < Tolerated,
            $"Il limite dichiarato era {Declared.TotalMilliseconds} ms, ne sono passati {clock.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task A_DNS_server_that_never_answers_gives_up_on_time()
    {
        Stopwatch clock = Stopwatch.StartNew();

        string name = await NameProbe.ReverseAsync("10.0.0.1", dnsServer: NoReverseRecord, Declared);

        clock.Stop();
        Assert.Equal(string.Empty, name);
        Assert.True(clock.Elapsed < Tolerated,
            $"Il limite dichiarato era {Declared.TotalMilliseconds} ms, ne sono passati {clock.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task Loopback_still_resolves_when_the_resolver_is_quick()
    {
        string name = await NameProbe.ReverseAsync("127.0.0.1", dnsServer: "", TimeSpan.FromSeconds(3));

        Assert.NotEqual("127.0.0.1", name);
    }
}
