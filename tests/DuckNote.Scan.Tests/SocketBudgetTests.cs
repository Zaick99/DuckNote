using System.Diagnostics;
using DuckNote.Scan;
using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

public class SocketBudgetTests
{
    [Fact]
    public async Task Slots_up_to_the_budget_are_handed_out_at_once()
    {
        using SocketBudget budget = new(3);

        IDisposable[] held =
        [
            await budget.TakeAsync(CancellationToken.None),
            await budget.TakeAsync(CancellationToken.None),
            await budget.TakeAsync(CancellationToken.None)
        ];

        Assert.Equal(3, held.Length);

        foreach (IDisposable slot in held)
        {
            slot.Dispose();
        }
    }

    [Fact]
    public async Task One_slot_past_the_budget_waits_for_a_release()
    {
        using SocketBudget budget = new(2);

        IDisposable first = await budget.TakeAsync(CancellationToken.None);
        IDisposable second = await budget.TakeAsync(CancellationToken.None);

        Task<IDisposable> third = budget.TakeAsync(CancellationToken.None);

        Assert.False(third.IsCompleted);

        first.Dispose();

        IDisposable given = await third;

        Assert.True(third.IsCompletedSuccessfully);

        given.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task Releasing_a_slot_twice_does_not_widen_the_budget()
    {
        using SocketBudget budget = new(1);

        IDisposable slot = await budget.TakeAsync(CancellationToken.None);
        slot.Dispose();
        slot.Dispose();

        IDisposable again = await budget.TakeAsync(CancellationToken.None);
        Task<IDisposable> queued = budget.TakeAsync(CancellationToken.None);

        Assert.False(queued.IsCompleted);

        again.Dispose();

        IDisposable last = await queued;
        last.Dispose();
    }

    [Fact]
    public async Task A_cancelled_wait_gives_up_instead_of_hanging()
    {
        using SocketBudget budget = new(1);
        using CancellationTokenSource stop = new();

        IDisposable held = await budget.TakeAsync(CancellationToken.None);
        Task<IDisposable> queued = budget.TakeAsync(stop.Token);

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        held.Dispose();
    }

    [Fact]
    public async Task Many_takers_never_exceed_the_budget_together()
    {
        const int budgeted = 8;
        const int takers = 200;

        using SocketBudget budget = new(budgeted);

        int inside = 0;
        int highest = 0;

        await Task.WhenAll(Enumerable.Range(0, takers).Select(async _ =>
        {
            using IDisposable slot = await budget.TakeAsync(CancellationToken.None);

            int now = Interlocked.Increment(ref inside);
            InterlockedMax(ref highest, now);

            await Task.Delay(1);

            Interlocked.Decrement(ref inside);
        }));

        Assert.True(highest <= budgeted, $"dentro insieme: {highest}, budget: {budgeted}");
        Assert.Equal(0, inside);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen = Volatile.Read(ref target);

        while (value > seen)
        {
            int was = Interlocked.CompareExchange(ref target, value, seen);

            if (was == seen)
            {
                return;
            }

            seen = was;
        }
    }

    [Fact]
    public async Task Ping_attempts_go_out_together_not_one_after_the_other()
    {
        TimeSpan patience = TimeSpan.FromMilliseconds(500);
        Stopwatch clock = Stopwatch.StartNew();

        PingOutcome outcome = await PingProbe.SendAsync(
            "192.0.2.1", count: 4, patience, CancellationToken.None);

        clock.Stop();

        Assert.False(outcome.Online);
        Assert.Equal(4, outcome.Sent);
        Assert.True(
            clock.Elapsed < patience * 2.5,
            $"quattro tentativi da {patience.TotalMilliseconds} ms hanno preso {clock.ElapsedMilliseconds} ms: " +
            "sembrano andati in sequenza");
    }
}
