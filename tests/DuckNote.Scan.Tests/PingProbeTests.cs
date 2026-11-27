using System.Net.NetworkInformation;
using System.Net.Sockets;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

/// <summary>
/// Il ping sotto carico: durante un'analisi grande gli host accesi non devono risultare spenti
/// perche' questo computer non ce la fa a mandare (o a ricevere) tutti i ping insieme.
/// </summary>
public class PingProbeTests
{
    [Theory]
    [InlineData(IPStatus.TimedOut)]
    [InlineData(IPStatus.DestinationHostUnreachable)]
    [InlineData(IPStatus.DestinationNetworkUnreachable)]
    [InlineData(IPStatus.TtlExpired)]
    public void An_answer_from_the_network_says_something_about_the_host(IPStatus status) =>
        Assert.False(PingProbe.IsLocalFailure(status));

    [Theory]
    [InlineData(IPStatus.NoResources)]
    [InlineData(IPStatus.HardwareError)]
    [InlineData(IPStatus.BadOption)]
    [InlineData(IPStatus.PacketTooBig)]
    [InlineData(IPStatus.Unknown)]
    public void A_failure_of_this_computer_says_nothing_about_the_host(IPStatus status) =>
        Assert.True(PingProbe.IsLocalFailure(status));

    [Fact]
    public void A_refusal_from_the_network_is_an_answer_but_running_out_of_buffers_is_not()
    {
        Assert.False(PingProbe.IsLocalFailure(new PingException("ping", new SocketException((int)SocketError.ConnectionRefused))));
        Assert.False(PingProbe.IsLocalFailure(new PingException("ping", new SocketException((int)SocketError.HostUnreachable))));
        Assert.True(PingProbe.IsLocalFailure(new PingException("ping", new SocketException((int)SocketError.NoBufferSpaceAvailable))));
        Assert.True(PingProbe.IsLocalFailure(new PingException("ping", new SocketException((int)SocketError.TooManyOpenSockets))));
        Assert.True(PingProbe.IsLocalFailure(new InvalidOperationException("ping")));
    }

    /// <summary>
    /// Qualcosa risponde a un ping verso <paramref name="address"/>? Su una rete vera un indirizzo
    /// di documentazione (192.0.2.0/24) resta muto; certe sandbox rispondono per ogni indirizzo, e
    /// li' le prove che hanno bisogno di un host muto non hanno niente da provare.
    /// </summary>
    internal static async Task<bool> SomethingAnswersFor(string address)
    {
        try
        {
            using Ping ping = new();
            PingReply reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(400), new byte[32], null, CancellationToken.None);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    [Fact]
    public async Task One_answer_is_enough_and_the_other_attempts_are_not_sent()
    {
        PingOutcome outcome = await PingProbe.SendAsync("127.0.0.1", 3, TimeSpan.FromSeconds(2));

        Assert.True(outcome.Online);
        Assert.False(outcome.Inconclusive);
        Assert.Equal(1, outcome.Sent);
        Assert.Equal(0, outcome.LossPercent);
    }

    [Fact]
    public async Task Hundreds_of_pings_at_once_all_get_their_answer()
    {
        // piu' del cancello (128): i ping in piu' aspettano il loro turno invece di perdersi
        PingOutcome[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, 300).Select(_ => PingProbe.SendAsync("127.0.0.1", 2, TimeSpan.FromSeconds(2))));

        Assert.All(outcomes, outcome => Assert.True(outcome.Online));
    }

    [Fact]
    public async Task A_host_that_cannot_answer_is_silent_not_inconclusive()
    {
        if (await SomethingAnswersFor("192.0.2.1"))
        {
            return;
        }

        PingOutcome outcome = await PingProbe.SendAsync("192.0.2.1", 2, TimeSpan.FromMilliseconds(300));

        Assert.False(outcome.Online);
        Assert.False(outcome.Inconclusive);
        Assert.Equal(2, outcome.Sent);
    }

    [Fact]
    public async Task Confirming_a_host_that_answers_keeps_it_online()
    {
        HostScanResult second = await HostProbe.ConfirmAsync("localhost", "127.0.0.1", [], TimeSpan.FromSeconds(1));

        Assert.Equal(HostStatus.Online, second.Status);
        Assert.Equal("localhost", second.Input);
        Assert.True(second.AverageMs >= 0);
    }

    [Fact]
    public async Task Confirming_a_host_that_stays_silent_says_it_is_unreachable()
    {
        if (await SomethingAnswersFor("192.0.2.1"))
        {
            return;
        }

        HostScanResult second = await HostProbe.ConfirmAsync("192.0.2.1", "192.0.2.1", [], TimeSpan.FromMilliseconds(300));

        Assert.False(second.IsAlive);
        Assert.Equal(HostStatus.Unreachable, second.Status);
    }
}
