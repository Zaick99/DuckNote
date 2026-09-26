using System.Diagnostics;
using DuckNote.Scan;

namespace DuckNote.Scan.Tests;

public class NetworkScannerTests
{
    private static readonly ScanOptions Quick = new()
    {
        PingCount = 1,
        PingTimeout = TimeSpan.FromMilliseconds(400),
        PortTimeout = TimeSpan.FromMilliseconds(300),
        Ports = [],
        ResolveDns = false,
        ProbeNetBios = false,
        ProbeMdns = false,
        ProbeSsdp = false,
        ProbeSnmp = false,
        ProbeBanners = false,
        ProbeShares = false,
        ProbeWmi = false
    };

    private static NetworkScanner Scanner(ScanOptions? options = null) =>
        new(options ?? Quick, IVendorLookup.None);

    private static async Task<List<HostScanResult>> CollectAsync(
        NetworkScanner scanner, IEnumerable<string> targets, CancellationToken cancellationToken = default)
    {
        List<HostScanResult> collected = [];
        await foreach (HostScanResult result in scanner.ScanAsync(targets, cancellationToken))
        {
            collected.Add(result);
        }
        return collected;
    }

    [Fact]
    public async Task Loopback_answers_and_is_reported_online()
    {
        List<HostScanResult> results = await CollectAsync(Scanner(), ["127.0.0.1"]);

        HostScanResult only = Assert.Single(results);
        Assert.Equal(HostStatus.Online, only.Status);
        Assert.True(only.IsAlive);
        Assert.Equal("127.0.0.1", only.Address);
    }

    [Fact]
    public async Task An_address_that_cannot_answer_is_reported_unreachable()
    {
        List<HostScanResult> results = await CollectAsync(Scanner(), ["192.0.2.1"]);

        HostScanResult only = Assert.Single(results);
        Assert.False(only.IsAlive);
        Assert.Equal(HostStatus.Unreachable, only.Status);
    }

    [Fact]
    public async Task Duplicates_and_blanks_never_reach_the_network()
    {
        List<HostScanResult> results = await CollectAsync(
            Scanner(), ["127.0.0.1", " 127.0.0.1 ", "", "   "]);

        Assert.Single(results);
    }

    [Fact]
    public async Task Nothing_to_scan_finishes_without_touching_anything()
    {
        Assert.Empty(await CollectAsync(Scanner(), []));
    }

    [Fact]
    public async Task Silent_hosts_are_waited_for_together_not_one_after_another()
    {
        string[] targets = [.. Enumerable.Range(1, 60).Select(n => $"192.0.2.{n}")];
        ScanOptions parallel = new()
        {
            Concurrency = 60,
            PingCount = 1,
            PingTimeout = TimeSpan.FromMilliseconds(500),
            Ports = [],
            ResolveDns = false,
            ProbeNetBios = false,
            ProbeMdns = false,
            ProbeSsdp = false,
            ProbeSnmp = false,
            ProbeBanners = false,
            ProbeShares = false,
            ProbeWmi = false
        };

        Stopwatch clock = Stopwatch.StartNew();
        List<HostScanResult> results = await CollectAsync(Scanner(parallel), targets);
        clock.Stop();

        Assert.Equal(60, results.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10),
            $"60 host in sequenza avrebbero richiesto ben oltre 10 s; impiegati {clock.Elapsed.TotalSeconds:F1} s.");
    }

    [Fact]
    public async Task Stopping_a_scan_stops_it()
    {
        string[] targets = [.. Enumerable.Range(1, 200).Select(n => $"192.0.2.{n}")];
        using CancellationTokenSource stop = new();
        stop.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(Scanner(), targets, stop.Token));
    }

    [Fact]
    public async Task An_open_port_is_found_and_named()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            ScanOptions withPort = new()
            {
                PingCount = 1,
                PingTimeout = TimeSpan.FromMilliseconds(400),
                PortTimeout = TimeSpan.FromMilliseconds(500),
                Ports = [port],
                ResolveDns = false,
                ProbeNetBios = false,
                ProbeMdns = false,
                ProbeSsdp = false,
                ProbeSnmp = false,
                ProbeBanners = false,
                ProbeShares = false,
                ProbeWmi = false
            };

            List<HostScanResult> results = await CollectAsync(Scanner(withPort), ["127.0.0.1"]);

            HostScanResult only = Assert.Single(results);
            Assert.Equal([port], only.OpenPorts);
            Assert.Contains($"{port}/", only.Services, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }
}
