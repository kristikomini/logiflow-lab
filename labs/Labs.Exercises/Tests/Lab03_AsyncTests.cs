using Labs.Exercises.Exercises;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 03. Do not edit — edit <c>Exercises/Lab03_Async.cs</c> until these pass.
/// </summary>
public sealed class Lab03AsyncTests
{
    // ── Exercise 1 — a timeout that does not leak ─────────────────────────────────────

    [Fact]
    public async Task WithTimeout_returns_the_result_when_the_task_is_fast()
    {
        int result = await Lab03Async.WithTimeoutAsync(
            Task.FromResult(42),
            TimeSpan.FromSeconds(5));

        result.ShouldBe(42);
    }

    [Fact]
    public async Task WithTimeout_throws_TimeoutException_when_the_task_is_slow()
    {
        var never = new TaskCompletionSource<int>();

        await Should.ThrowAsync<TimeoutException>(() =>
            Lab03Async.WithTimeoutAsync(never.Task, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task WithTimeout_prefers_cancellation_over_timeout()
    {
        var never = new TaskCompletionSource<int>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Lab03Async.WithTimeoutAsync(never.Task, TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task WithTimeout_lets_the_original_exception_through_unwrapped()
    {
        // Not an AggregateException. This is the .Result trap from module 21, avoided.
        Task<int> failing = Task.FromException<int>(new InvalidOperationException("boom"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            Lab03Async.WithTimeoutAsync(failing, TimeSpan.FromSeconds(5)));

        ex.Message.ShouldBe("boom");
    }

    // ── Exercise 2 — retry with backoff ───────────────────────────────────────────────

    [Fact]
    public async Task Retry_returns_immediately_when_the_first_attempt_succeeds()
    {
        int calls = 0;

        int result = await Lab03Async.RetryAsync(
            _ => { calls++; return Task.FromResult(7); },
            maxAttempts: 3,
            shouldRetry: _ => true,
            delay: _ => TimeSpan.Zero);

        result.ShouldBe(7);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_retries_until_it_succeeds()
    {
        int calls = 0;

        int result = await Lab03Async.RetryAsync(
            _ =>
            {
                calls++;
                return calls < 3
                    ? Task.FromException<int>(new HttpRequestException("transient"))
                    : Task.FromResult(7);
            },
            maxAttempts: 5,
            shouldRetry: _ => true,
            delay: _ => TimeSpan.Zero);

        result.ShouldBe(7);
        calls.ShouldBe(3);
    }

    [Fact]
    public async Task Retry_counts_the_first_attempt_towards_the_maximum()
    {
        int calls = 0;

        await Should.ThrowAsync<HttpRequestException>(() =>
            Lab03Async.RetryAsync<int>(
                _ => { calls++; return Task.FromException<int>(new HttpRequestException("down")); },
                maxAttempts: 3,
                shouldRetry: _ => true,
                delay: _ => TimeSpan.Zero));

        calls.ShouldBe(3);   // three total, not four
    }

    [Fact]
    public async Task Retry_does_not_retry_what_it_was_told_not_to()
    {
        // A 400 will never become a 200. Retrying it multiplies one bad request.
        int calls = 0;

        await Should.ThrowAsync<ArgumentException>(() =>
            Lab03Async.RetryAsync<int>(
                _ => { calls++; return Task.FromException<int>(new ArgumentException("bad request")); },
                maxAttempts: 5,
                shouldRetry: ex => ex is HttpRequestException,
                delay: _ => TimeSpan.Zero));

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_throws_the_last_exception_with_its_message()
    {
        int calls = 0;

        var ex = await Should.ThrowAsync<HttpRequestException>(() =>
            Lab03Async.RetryAsync<int>(
                _ =>
                {
                    calls++;
                    return Task.FromException<int>(new HttpRequestException($"attempt {calls}"));
                },
                maxAttempts: 3,
                shouldRetry: _ => true,
                delay: _ => TimeSpan.Zero));

        ex.Message.ShouldBe("attempt 3");
    }

    [Fact]
    public async Task Retry_stops_when_the_caller_cancels()
    {
        using var cts = new CancellationTokenSource();
        int calls = 0;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Lab03Async.RetryAsync<int>(
                _ =>
                {
                    calls++;
#pragma warning disable CA1849 // the operation body is synchronous here on purpose
                    cts.Cancel();
#pragma warning restore CA1849
                    return Task.FromException<int>(new HttpRequestException("transient"));
                },
                maxAttempts: 10,
                shouldRetry: _ => true,
                delay: _ => TimeSpan.Zero,
                cancellationToken: cts.Token));

        calls.ShouldBe(1);
    }

    // ── Exercise 3 — bounded concurrency ──────────────────────────────────────────────

    [Fact]
    public async Task Map_returns_results_in_the_original_order()
    {
        IReadOnlyList<int> results = await Lab03Async.MapWithConcurrencyLimitAsync(
            [1, 2, 3, 4, 5],
            async (n, ct) =>
            {
                await Task.Delay(6 - n, ct);   // the slowest item is first
                return n * 10;
            },
            maxConcurrency: 5);

        results.ShouldBe([10, 20, 30, 40, 50]);
    }

    [Fact]
    public async Task Map_never_exceeds_the_concurrency_limit()
    {
        int running = 0;
        int peak = 0;
        var gate = new Lock();

        await Lab03Async.MapWithConcurrencyLimitAsync(
            Enumerable.Range(0, 50),
            async (n, ct) =>
            {
                lock (gate)
                {
                    running++;
                    peak = Math.Max(peak, running);
                }

                await Task.Delay(5, ct);

                lock (gate)
                {
                    running--;
                }

                return n;
            },
            maxConcurrency: 4);

        peak.ShouldBeLessThanOrEqualTo(4);
        peak.ShouldBeGreaterThan(1);   // and it really is concurrent, not a serial loop
    }

    [Fact]
    public async Task Map_releases_the_semaphore_even_when_an_item_fails()
    {
        // If Release() is not in a finally, the limit shrinks with every failure and the
        // whole thing eventually deadlocks. This test would hang rather than fail.
        Task work = Lab03Async.MapWithConcurrencyLimitAsync<int, int>(
            Enumerable.Range(0, 20),
            (n, _) => n < 4
                ? Task.FromException<int>(new InvalidOperationException("boom"))
                : Task.FromResult(n),
            maxConcurrency: 2);

        Task finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.ShouldBe(work, "the semaphore was not released on the failure path");
        await Should.ThrowAsync<Exception>(() => work);
    }

    [Fact]
    public async Task Map_handles_an_empty_source()
    {
        IReadOnlyList<int> results = await Lab03Async.MapWithConcurrencyLimitAsync(
            Array.Empty<int>(),
            (n, _) => Task.FromResult(n),
            maxConcurrency: 4);

        results.ShouldBeEmpty();
    }

    // ── Exercise 4 — collecting every error ───────────────────────────────────────────

    [Fact]
    public async Task WhenAllCollecting_returns_every_result_in_order()
    {
        IReadOnlyList<int> results = await Lab03Async.WhenAllCollectingErrorsAsync(
            [Task.FromResult(1), Task.FromResult(2), Task.FromResult(3)]);

        results.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task WhenAllCollecting_reports_all_the_failures_not_just_the_first()
    {
        var ex = await Should.ThrowAsync<AggregateException>(() =>
            Lab03Async.WhenAllCollectingErrorsAsync(
            [
                Task.FromException<int>(new InvalidOperationException("first")),
                Task.FromResult(2),
                Task.FromException<int>(new InvalidOperationException("second")),
                Task.FromException<int>(new InvalidOperationException("third")),
            ]));

        ex.InnerExceptions.Count.ShouldBe(3);
        ex.InnerExceptions.Select(e => e.Message).ShouldBe(["first", "second", "third"], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAllCollecting_waits_for_every_task_before_throwing()
    {
        var slow = new TaskCompletionSource<int>();
        bool slowObserved = false;

        Task work = Lab03Async.WhenAllCollectingErrorsAsync(
        [
            Task.FromException<int>(new InvalidOperationException("fast failure")),
            slow.Task,
        ]);

        await Task.Delay(50);
        work.IsCompleted.ShouldBeFalse("it must not short-circuit on the first failure");

        slow.SetResult(1);
        slowObserved = true;

        await Should.ThrowAsync<AggregateException>(() => work);
        slowObserved.ShouldBeTrue();
    }
}
