using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiFlow.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="QueuedEmail"/>.</summary>
/// <remarks>
/// <para>
/// <b>Two indexes, and both are the difference between a queue and a table.</b> The delivery
/// worker asks one question every few seconds — "what is due?" — and the answer must not cost a
/// scan of a table that also holds a month of delivered history. The unique index on the
/// deduplication key is not a performance concern at all: it is the correctness guarantee behind
/// <see cref="Application.Abstractions.Mailing.EmailMessage.DeduplicationKey"/>.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-07-sql-and-transactions/</c>
/// </remarks>
public sealed class QueuedEmailConfiguration : IEntityTypeConfiguration<QueuedEmail>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<QueuedEmail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("QueuedEmails");
        builder.HasKey(email => email.Id);

        builder.Property(email => email.Recipient).HasMaxLength(EmailAddress.MaxLength).IsRequired();

        builder.Property(email => email.Subject)
            .HasMaxLength(Application.Abstractions.Mailing.EmailMessage.MaxSubjectLength)
            .IsRequired();

        // The two places nvarchar(max) is right on this table. A body has no sensible upper
        // bound, and the model-wide 500-character default would silently truncate every message
        // to its first paragraph - a bug that only shows up in the customer's inbox.
        builder.Property(email => email.TextBody).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(email => email.HtmlBody).HasColumnType("nvarchar(max)");

        builder.Property(email => email.Category).HasMaxLength(50).IsRequired();
        builder.Property(email => email.DeduplicationKey).HasMaxLength(200);

        builder.Property(email => email.QueuedAtUtc).IsRequired();
        builder.Property(email => email.NextAttemptUtc).IsRequired();
        builder.Property(email => email.SentAtUtc);
        builder.Property(email => email.AbandonedAtUtc);
        builder.Property(email => email.AttemptCount).IsRequired();
        builder.Property(email => email.LastError).HasMaxLength(QueuedEmail.MaxErrorLength);

        // ── The delivery worker's only query ─────────────────────────────────────────────────
        // A filtered index on exactly the rows that are still in play. Within a week of going
        // live the overwhelming majority of rows are delivered history, and this index does not
        // contain a single one of them - so the "what is due?" seek stays the same size whether
        // the table holds ten thousand rows or ten million.
        //
        // The include column matters too: with AttemptCount carried in the index leaf, the claim
        // statement never touches the base table to read it, and the row's several kilobytes of
        // body are not pulled into memory to decide whether to send it.
        builder.HasIndex(email => email.NextAttemptUtc)
            .HasFilter("[SentAtUtc] IS NULL AND [AbandonedAtUtc] IS NULL")
            .IncludeProperties(email => email.AttemptCount)
            .HasDatabaseName("IX_QueuedEmails_Due");

        // ── The idempotency guarantee ────────────────────────────────────────────────────────
        // UNIQUE, and filtered so that the many rows with no key do not all collide with each
        // other on NULL. SQL Server treats NULLs as equal for uniqueness - unlike the SQL
        // standard, and unlike PostgreSQL - so without the filter the second keyless message
        // ever queued would be rejected. It is a genuinely surprising piece of T-SQL behaviour
        // and this is exactly where it bites.
        builder.HasIndex(email => email.DeduplicationKey)
            .IsUnique()
            .HasFilter("[DeduplicationKey] IS NOT NULL")
            .HasDatabaseName("UX_QueuedEmails_DeduplicationKey");
    }
}
