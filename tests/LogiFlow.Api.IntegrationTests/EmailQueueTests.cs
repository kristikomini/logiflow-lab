using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// Tests for the email queue against a real SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every claim the mailing system makes that a unit test cannot check is checked here.</b>
/// That the message is written in the caller's transaction and disappears when it rolls back;
/// that the unique index actually stops a duplicate; and — the important one — that two workers
/// claiming concurrently split the queue instead of both sending everything.
/// </para>
/// <para>
/// <b>Why none of this can be tested with a fake.</b> <c>READPAST</c>, <c>UPDLOCK</c> and
/// <c>OUTPUT INSERTED</c> are behaviours of a lock manager. A substitute would return whatever
/// the test author assumed they do, which is precisely the assumption under test. The in-memory
/// provider is worse than useless here: it has no locks and no unique indexes, so it would
/// report success for a queue that duplicates every message in production.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-12-testing/</c>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class EmailQueueTests(LogiFlowApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static EmailMessage AMessage(string? deduplicationKey = null, string to = "mario@rossi.it") =>
        EmailMessage.Create(
            EmailAddress.Create(to).Value,
            "Order shipped",
            "Your order is on its way.",
            "<p>Your order is on its way.</p>",
            "shipment-dispatched",
            deduplicationKey);

    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope();

    [Fact]
    public async Task EnqueueThenCommit_WritesTheMessage()
    {
        string key = $"test:{Guid.CreateVersion7():N}";

        await using (AsyncServiceScope scope = Scope())
        {
            LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
            IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

            (await queue.EnqueueAsync(AMessage(key), CancellationToken.None)).ShouldBeTrue();

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using AsyncServiceScope check = Scope();

        QueuedEmail? stored = await check.ServiceProvider
            .GetRequiredService<LogiFlowDbContext>()
            .QueuedEmails
            .SingleOrDefaultAsync(email => email.DeduplicationKey == key, CancellationToken.None);

        stored.ShouldNotBeNull();
        stored.SentAtUtc.ShouldBeNull();
        stored.AttemptCount.ShouldBe(0);
        stored.HtmlBody.ShouldNotBeNull();
    }

    /// <summary>
    /// The headline guarantee: the message is part of the caller's transaction, so a business
    /// operation that fails cannot leave a customer holding an email about it.
    /// </summary>
    [Fact]
    public async Task EnqueueThenRollback_WritesNothing()
    {
        string key = $"test:{Guid.CreateVersion7():N}";

        await using (AsyncServiceScope scope = Scope())
        {
            LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
            IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

            // ── The execution strategy is not optional here ─────────────────────────────────
            // The DbContext is configured with EnableRetryOnFailure, and EF refuses a
            // user-initiated transaction under a retrying strategy:
            //
            //   The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not
            //   support user-initiated transactions.
            //
            // The reason is sound: a retry has to re-run the WHOLE transaction, and EF cannot
            // replay statements it did not issue. Wrapping the transaction in the strategy makes
            // the unit of retry the transaction rather than the statement. This is the same trap
            // IUnitOfWork.ExecuteInTransactionAsync exists to handle, met here in a test.
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(CancellationToken.None);

                await queue.EnqueueAsync(AMessage(key), CancellationToken.None);
                await context.SaveChangesAsync(CancellationToken.None);

                await transaction.RollbackAsync(CancellationToken.None);
            });
        }

        await using AsyncServiceScope check = Scope();

        bool exists = await check.ServiceProvider
            .GetRequiredService<LogiFlowDbContext>()
            .QueuedEmails
            .AnyAsync(email => email.DeduplicationKey == key, CancellationToken.None);

        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Enqueue_DropsADuplicateDeduplicationKey()
    {
        string key = $"test:{Guid.CreateVersion7():N}";

        await using (AsyncServiceScope first = Scope())
        {
            LogiFlowDbContext context = first.ServiceProvider.GetRequiredService<LogiFlowDbContext>();

            await first.ServiceProvider
                .GetRequiredService<IEmailQueue>()
                .EnqueueAsync(AMessage(key), CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using AsyncServiceScope second = Scope();

        bool queued = await second.ServiceProvider
            .GetRequiredService<IEmailQueue>()
            .EnqueueAsync(AMessage(key), CancellationToken.None);

        queued.ShouldBeFalse();
    }

    /// <summary>
    /// The case the change-tracker check exists for: two enqueues in ONE transaction, where the
    /// database still holds nothing and a naive EXISTS check would let both through — failing
    /// later on the unique index, in a message that names neither call site.
    /// </summary>
    [Fact]
    public async Task Enqueue_DropsADuplicateWithinTheSameTransaction()
    {
        string key = $"test:{Guid.CreateVersion7():N}";

        await using AsyncServiceScope scope = Scope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

        (await queue.EnqueueAsync(AMessage(key), CancellationToken.None)).ShouldBeTrue();
        (await queue.EnqueueAsync(AMessage(key), CancellationToken.None)).ShouldBeFalse();

        // The save must succeed. If the second enqueue had been allowed through, this would
        // throw a DbUpdateException on the unique index and take the caller's transaction with it.
        await context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Enqueue_AllowsManyMessagesWithNoDeduplicationKey()
    {
        await using AsyncServiceScope scope = Scope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

        await queue.EnqueueAsync(AMessage(deduplicationKey: null), CancellationToken.None);
        await queue.EnqueueAsync(AMessage(deduplicationKey: null), CancellationToken.None);

        // SQL Server treats NULLs as EQUAL for uniqueness - unlike the standard - so without the
        // filtered index this second save would fail. The behaviour is surprising enough to be
        // worth a test that fails loudly if somebody "tidies up" the filter.
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Two workers, one queue. Every message must be claimed exactly once.
    /// </summary>
    [Fact]
    public async Task ClaimBatch_NeverHandsTheSameMessageToTwoWorkers()
    {
        const int messages = 40;

        await using (AsyncServiceScope seed = Scope())
        {
            LogiFlowDbContext context = seed.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
            IEmailQueue queue = seed.ServiceProvider.GetRequiredService<IEmailQueue>();

            for (var i = 0; i < messages; i++)
            {
                await queue.EnqueueAsync(
                    AMessage($"claim:{Guid.CreateVersion7():N}"),
                    CancellationToken.None);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        // Two independent scopes, and therefore two independent connections - the same shape as
        // two application instances behind a load balancer.
        await using AsyncServiceScope workerA = Scope();
        await using AsyncServiceScope workerB = Scope();

        Task<IReadOnlyList<Guid>> claimA = QueuedEmailStore.ClaimBatchAsync(
            workerA.ServiceProvider.GetRequiredService<LogiFlowDbContext>(),
            Now.AddYears(1),
            messages,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Task<IReadOnlyList<Guid>> claimB = QueuedEmailStore.ClaimBatchAsync(
            workerB.ServiceProvider.GetRequiredService<LogiFlowDbContext>(),
            Now.AddYears(1),
            messages,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        IReadOnlyList<Guid>[] claims = await Task.WhenAll(claimA, claimB);

        Guid[] all = [.. claims[0], .. claims[1]];

        // The two workers may split the queue any way they like - READPAST makes the division
        // non-deterministic, which is the point - but no id may appear in both.
        all.Distinct().Count().ShouldBe(all.Length);
    }

    [Fact]
    public async Task ClaimBatch_TakesTheAttemptAndHidesTheMessageBehindItsLease()
    {
        string key = $"claim:{Guid.CreateVersion7():N}";

        await using AsyncServiceScope scope = Scope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();

        await scope.ServiceProvider
            .GetRequiredService<IEmailQueue>()
            .EnqueueAsync(AMessage(key), CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);

        DateTimeOffset now = Now.AddYears(1);

        IReadOnlyList<Guid> firstClaim = await QueuedEmailStore.ClaimBatchAsync(
            context, now, batchSize: 100, TimeSpan.FromMinutes(5), CancellationToken.None);

        QueuedEmail claimed = await context.QueuedEmails
            .AsNoTracking()
            .SingleAsync(email => email.DeduplicationKey == key, CancellationToken.None);

        firstClaim.ShouldContain(claimed.Id);

        // The attempt is counted at claim time, so a message that crashes the worker still runs
        // out of lives rather than being retried forever.
        claimed.AttemptCount.ShouldBe(1);

        // And it is invisible until the lease expires: a second claim one second later must not
        // see it, or a slow send would be delivered twice.
        IReadOnlyList<Guid> secondClaim = await QueuedEmailStore.ClaimBatchAsync(
            context, now.AddSeconds(1), batchSize: 100, TimeSpan.FromMinutes(5), CancellationToken.None);

        secondClaim.ShouldNotContain(claimed.Id);

        // Once it has, the message is due again - which is what makes a crashed worker's
        // in-flight messages recoverable rather than lost.
        IReadOnlyList<Guid> afterLease = await QueuedEmailStore.ClaimBatchAsync(
            context, now.AddMinutes(6), batchSize: 100, TimeSpan.FromMinutes(5), CancellationToken.None);

        afterLease.ShouldContain(claimed.Id);
    }

    [Fact]
    public async Task ClaimBatch_IgnoresDeliveredAndAbandonedMessages()
    {
        await using AsyncServiceScope scope = Scope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

        string sentKey = $"claim:{Guid.CreateVersion7():N}";
        string deadKey = $"claim:{Guid.CreateVersion7():N}";

        await queue.EnqueueAsync(AMessage(sentKey), CancellationToken.None);
        await queue.EnqueueAsync(AMessage(deadKey), CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);

        QueuedEmail sent = await context.QueuedEmails
            .SingleAsync(email => email.DeduplicationKey == sentKey, CancellationToken.None);

        QueuedEmail dead = await context.QueuedEmails
            .SingleAsync(email => email.DeduplicationKey == deadKey, CancellationToken.None);

        sent.MarkSent(Now);
        dead.MarkFailed("550 mailbox unavailable", Now, isTransient: false, new DeliveryOptions());

        await context.SaveChangesAsync(CancellationToken.None);

        IReadOnlyList<Guid> claimed = await QueuedEmailStore.ClaimBatchAsync(
            context, Now.AddYears(1), batchSize: 500, TimeSpan.FromMinutes(5), CancellationToken.None);

        claimed.ShouldNotContain(sent.Id);
        claimed.ShouldNotContain(dead.Id);
    }

    [Fact]
    public async Task DeleteDeliveredBefore_KeepsAbandonedMessagesForever()
    {
        await using AsyncServiceScope scope = Scope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        IEmailQueue queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

        string sentKey = $"sweep:{Guid.CreateVersion7():N}";
        string deadKey = $"sweep:{Guid.CreateVersion7():N}";

        await queue.EnqueueAsync(AMessage(sentKey), CancellationToken.None);
        await queue.EnqueueAsync(AMessage(deadKey), CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);

        QueuedEmail sent = await context.QueuedEmails
            .SingleAsync(email => email.DeduplicationKey == sentKey, CancellationToken.None);

        QueuedEmail dead = await context.QueuedEmails
            .SingleAsync(email => email.DeduplicationKey == deadKey, CancellationToken.None);

        sent.MarkSent(Now.AddYears(-1));
        dead.MarkFailed("550 mailbox unavailable", Now.AddYears(-1), isTransient: false, new DeliveryOptions());

        await context.SaveChangesAsync(CancellationToken.None);

        await QueuedEmailStore.DeleteDeliveredBeforeAsync(
            context, Now, batchSize: 1000, CancellationToken.None);

        // A message the customer never received is evidence. Retention removes history, not
        // failures - a cleanup job that erases its own bad news is worse than no cleanup job.
        (await context.QueuedEmails.AnyAsync(e => e.Id == sent.Id, CancellationToken.None)).ShouldBeFalse();
        (await context.QueuedEmails.AnyAsync(e => e.Id == dead.Id, CancellationToken.None)).ShouldBeTrue();
    }
}
