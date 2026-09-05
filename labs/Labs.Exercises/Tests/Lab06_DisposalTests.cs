using Labs.Exercises.Exercises;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 06. Do not edit — edit
/// <c>Exercises/Lab06_Disposal.cs</c> until these pass.
/// </summary>
public sealed class Lab06DisposalTests
{
    // ── Exercise 1 — idempotent disposal ──────────────────────────────────────────────

    [Fact]
    public void Taking_a_lease_reduces_availability()
    {
        var pool = new Lab06Disposal.LeasePool(capacity: 2);

        using Lab06Disposal.Lease lease = pool.Take();

        pool.Available.ShouldBe(1);
    }

    [Fact]
    public void Disposing_returns_the_lease()
    {
        var pool = new Lab06Disposal.LeasePool(capacity: 2);

        using (pool.Take())
        {
            pool.Available.ShouldBe(1);
        }

        pool.Available.ShouldBe(2);
    }

    [Fact]
    public void Disposing_twice_returns_the_lease_once()
    {
        var pool = new Lab06Disposal.LeasePool(capacity: 1);
        Lab06Disposal.Lease lease = pool.Take();

        lease.Dispose();
        lease.Dispose();
        lease.Dispose();

        pool.Available.ShouldBe(1, "a double return corrupts the pool count");
    }

    [Fact]
    public void Using_a_disposed_lease_throws()
    {
        var pool = new Lab06Disposal.LeasePool(capacity: 1);
        Lab06Disposal.Lease lease = pool.Take();
        lease.Dispose();

        Should.Throw<ObjectDisposedException>(() => lease.Use());
    }

    [Fact]
    public void An_exhausted_pool_refuses()
    {
        var pool = new Lab06Disposal.LeasePool(capacity: 1);
        using Lab06Disposal.Lease held = pool.Take();

        Should.Throw<InvalidOperationException>(() => pool.Take());
    }

    // ── Exercise 2 — async disposal ───────────────────────────────────────────────────

    [Fact]
    public async Task Nothing_is_flushed_before_disposal()
    {
        var flushes = new List<IReadOnlyList<string>>();
        var writer = new Lab06Disposal.AsyncBatchWriter(batch =>
        {
            flushes.Add(batch);
            return Task.CompletedTask;
        });

        writer.Add("one");
        writer.Add("two");

        flushes.ShouldBeEmpty();

        await writer.DisposeAsync();

        flushes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Disposal_flushes_in_insertion_order()
    {
        IReadOnlyList<string>? batch = null;
        var writer = new Lab06Disposal.AsyncBatchWriter(b => { batch = b; return Task.CompletedTask; });

        writer.Add("one");
        writer.Add("two");
        writer.Add("three");
        await writer.DisposeAsync();

        batch.ShouldBe(["one", "two", "three"]);
    }

    [Fact]
    public async Task Await_using_flushes_even_when_the_body_throws()
    {
        var flushed = 0;

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var writer = new Lab06Disposal.AsyncBatchWriter(
                _ => { flushed++; return Task.CompletedTask; });

            writer.Add("one");
            throw new InvalidOperationException("boom");
        });

        flushed.ShouldBe(1, "await using compiles to try/finally");
    }

    [Fact]
    public async Task Disposing_twice_flushes_once()
    {
        var flushes = 0;
        var writer = new Lab06Disposal.AsyncBatchWriter(_ => { flushes++; return Task.CompletedTask; });
        writer.Add("one");

        await writer.DisposeAsync();
        await writer.DisposeAsync();

        flushes.ShouldBe(1);
    }

    [Fact]
    public async Task Disposing_an_empty_writer_does_not_call_flush()
    {
        var flushes = 0;
        var writer = new Lab06Disposal.AsyncBatchWriter(_ => { flushes++; return Task.CompletedTask; });

        await writer.DisposeAsync();

        flushes.ShouldBe(0, "do not make an I/O call to write nothing");
    }

    [Fact]
    public async Task Adding_after_disposal_throws()
    {
        var writer = new Lab06Disposal.AsyncBatchWriter(_ => Task.CompletedTask);
        await writer.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => writer.Add("late"));
    }

    // ── Exercise 3 — the race ─────────────────────────────────────────────────────────

    [Fact]
    public void Counter_is_exact_under_contention()
    {
        var counter = new Lab06Disposal.SafeCounter();

        Parallel.For(0, 200_000, _ => counter.Increment());

        counter.Value.ShouldBe(200_000, "a bare ++ loses increments, and loses a different number each run");
    }

    [Fact]
    public async Task Counter_is_exact_across_explicit_tasks()
    {
        var counter = new Lab06Disposal.SafeCounter();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 25_000; i++)
            {
                counter.Increment();
            }
        })));

        counter.Value.ShouldBe(200_000);
    }

    [Fact]
    public void Counter_starts_at_zero()
        => new Lab06Disposal.SafeCounter().Value.ShouldBe(0);

    // ── Exercise 4 — single flight ────────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_misses_for_one_key_load_once()
    {
        var loads = 0;
        var cache = new Lab06Disposal.SingleFlightCache<string, int>(async _ =>
        {
            Interlocked.Increment(ref loads);
            await Task.Delay(50);          // long enough for everyone else to arrive and miss
            return 42;
        });

        int[] results = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => cache.GetAsync("orders")));

        results.ShouldAllBe(r => r == 42);
        loads.ShouldBe(1, "this is the cache stampede — fifty callers, one load");
    }

    [Fact]
    public async Task A_second_call_after_loading_does_not_load_again()
    {
        var loads = 0;
        var cache = new Lab06Disposal.SingleFlightCache<string, int>(_ =>
        {
            loads++;
            return Task.FromResult(7);
        });

        (await cache.GetAsync("a")).ShouldBe(7);
        (await cache.GetAsync("a")).ShouldBe(7);

        loads.ShouldBe(1);
    }

    [Fact]
    public async Task Different_keys_load_independently()
    {
        var loads = 0;
        var cache = new Lab06Disposal.SingleFlightCache<string, string>(key =>
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(key.ToUpperInvariant());
        });

        (await cache.GetAsync("a")).ShouldBe("A");
        (await cache.GetAsync("b")).ShouldBe("B");

        loads.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_load_is_not_cached()
    {
        var attempts = 0;
        var cache = new Lab06Disposal.SingleFlightCache<string, int>(_ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<int>(new InvalidOperationException("transient"))
                : Task.FromResult(99);
        });

        await Should.ThrowAsync<InvalidOperationException>(() => cache.GetAsync("a"));

        (await cache.GetAsync("a")).ShouldBe(99, "caching the failure would make a blip permanent");
        attempts.ShouldBe(2);
    }
}
