using LogiFlow.Academy.Api.Domain;
using LogiFlow.Academy.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Academy.Api.Persistence;

/// <summary>The Academy's own database. Four tables, no relationship to the LogiFlow domain.</summary>
/// <param name="options">Provider and connection, chosen in <c>Program.cs</c>.</param>
public sealed class AcademyDbContext(DbContextOptions<AcademyDbContext> options) : DbContext(options)
{
    /// <summary>Registered learners.</summary>
    public DbSet<AcademyUser> Users => Set<AcademyUser>();

    /// <summary>One progress document per learner.</summary>
    public DbSet<LearnerProfile> Profiles => Set<LearnerProfile>();

    /// <summary>Issued refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Issued password-reset tickets.</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AcademyUser>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);

            // Unique, and the lookup path for every sign-in and every reset. Stored already
            // normalised (lower-cased, trimmed) rather than compared with a function, because
            // WHERE LOWER(Username) = ... cannot use this index. Same SARGability rule as
            // course/module-07-sql-and-transactions — normalise on write, seek on read.
            user.HasIndex(u => u.Username).IsUnique();
            user.Property(u => u.Username).HasMaxLength(Usernames.MaximumLength).IsRequired();
            user.Property(u => u.DisplayName).HasMaxLength(60).IsRequired();
            user.Property(u => u.PasswordHash).HasMaxLength(128).IsRequired();
            user.Property(u => u.PasswordSalt).HasMaxLength(64).IsRequired();
            user.Property(u => u.RecoveryCodeHash).HasMaxLength(64).IsRequired();

            user.HasOne(u => u.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<LearnerProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            user.HasMany(u => u.RefreshTokens)
                .WithOne(t => t.User)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            user.HasMany(u => u.PasswordResetTokens)
                .WithOne(t => t.User)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearnerProfile>(profile =>
        {
            profile.ToTable("Profiles");
            profile.HasKey(p => p.UserId);

            // No length limit: this is the learner's whole progress document. It is a few
            // kilobytes today and grows with the number of questions answered.
            profile.Property(p => p.Document).IsRequired();

            // The leaderboard's only ordering. Without it, every leaderboard read is a scan
            // of the whole table — invisible with twelve users and not with twelve thousand.
            profile.HasIndex(p => p.Xp).IsDescending();
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.ToTable("RefreshTokens");
            token.HasKey(t => t.Id);

            // Every refresh request looks a token up by its hash, so this is the hot path.
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            token.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
        });

        modelBuilder.Entity<PasswordResetToken>(reset =>
        {
            reset.ToTable("PasswordResetTokens");
            reset.HasKey(t => t.Id);

            // Redeeming a ticket looks it up by hash and nothing else, so this is the only
            // index the table needs — and unique, because two tickets hashing the same would
            // mean the RNG had stopped being one.
            reset.HasIndex(t => t.TokenHash).IsUnique();
            reset.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        });
    }
}
