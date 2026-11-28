using System.Net;
using System.Net.Sockets;
using DuckNote.Scan;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

/// <summary>La prova di vita sulle porte: un host che non risponde al ping puo' essere acceso.</summary>
public class LivenessTests
{
    [Fact]
    public async Task A_port_that_accepts_says_the_host_is_on()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(await PortProbe.AnswersAsync("127.0.0.1", [port], TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task A_port_that_refuses_also_says_the_host_is_on()
    {
        // una porta libera: il sistema rifiuta la connessione, cioe' l'host risponde "qui no"
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.True(await PortProbe.AnswersAsync("127.0.0.1", [port], TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task No_ports_prove_nothing()
    {
        Assert.False(await PortProbe.AnswersAsync("127.0.0.1", [], TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Silent_hosts_are_knocked_on_by_default_and_the_common_ports_include_phones()
    {
        ScanOptions options = new();

        Assert.True(options.ProbePortsWhenSilent);
        Assert.Contains(62078, ScanOptions.LivenessPorts);
        Assert.Contains(445, ScanOptions.LivenessPorts);
        Assert.Contains(62078, DeviceIdentity.IdentityPorts);
    }
}
