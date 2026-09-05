using LogiFlow.Academy.Api.Security;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// Tests for the part of this service that would matter most if it were wrong.
/// </summary>
/// <remarks>
/// Every one of these is a property somebody has shipped a service without. They are cheap to
/// write and they are the difference between "we hash passwords" and knowing what that means.
/// </remarks>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_accepts_the_original_password()
    {
        PasswordHasher.HashResult stored = PasswordHasher.Hash("correct horse battery staple");

        PasswordHasher.Verify("correct horse battery staple", stored.Hash, stored.Salt, stored.Iterations)
            .ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_a_wrong_password()
    {
        PasswordHasher.HashResult stored = PasswordHasher.Hash("correct horse battery staple");

        PasswordHasher.Verify("Correct horse battery staple", stored.Hash, stored.Salt, stored.Iterations)
            .ShouldBeFalse();
    }

    [Fact]
    public void The_same_password_hashes_differently_for_two_users()
    {
        // The point of a per-user salt. Without it, two people who chose the same password have
        // the same row, one rainbow table breaks both, and the dump itself tells an attacker
        // which accounts to try first.
        PasswordHasher.HashResult a = PasswordHasher.Hash("same password");
        PasswordHasher.HashResult b = PasswordHasher.Hash("same password");

        a.Salt.ShouldNotBe(b.Salt);
        a.Hash.ShouldNotBe(b.Hash);
    }

    [Fact]
    public void The_plaintext_never_appears_in_what_is_stored()
    {
        PasswordHasher.HashResult stored = PasswordHasher.Hash("hunter2-hunter2-hunter2");

        stored.Hash.ShouldNotContain("hunter2");
        stored.Salt.ShouldNotContain("hunter2");
    }

    [Fact]
    public void Verify_fails_closed_on_a_corrupted_row()
    {
        // A row that has been truncated or mangled must return false, not throw. Throwing turns
        // into a 500 that tells the caller this particular account exists and is broken.
        PasswordHasher.Verify("anything", "not base64 at all!!", "nor is this!!", 210_000)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Verify_rejects_an_empty_candidate(string? candidate)
    {
        PasswordHasher.HashResult stored = PasswordHasher.Hash("a real password here");

        PasswordHasher.Verify(candidate!, stored.Hash, stored.Salt, stored.Iterations).ShouldBeFalse();
    }

    [Fact]
    public void A_hash_made_with_a_lower_cost_is_flagged_for_upgrade_and_still_verifies()
    {
        // The whole reason the iteration count is stored per user: an account created when the
        // recommended cost was lower must keep working AND be identifiable for re-hashing.
        PasswordHasher.HashResult old = PasswordHasher.Hash("an old account's password", iterations: 50_000);

        PasswordHasher.Verify("an old account's password", old.Hash, old.Salt, old.Iterations).ShouldBeTrue();
        PasswordHasher.NeedsUpgrade(old.Iterations).ShouldBeTrue();
        PasswordHasher.NeedsUpgrade(PasswordHasher.DefaultIterations).ShouldBeFalse();
    }

    [Fact]
    public void Hash_refuses_an_absurdly_low_iteration_count()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("password", iterations: 1));
    }

    [Fact]
    public void The_default_cost_meets_the_current_OWASP_floor()
    {
        // A number that silently drifts down over the years is how a service ends up with fast
        // hashes nobody decided on. Assert it, so lowering it is a deliberate act with a diff.
        PasswordHasher.DefaultIterations.ShouldBeGreaterThanOrEqualTo(210_000);
    }
}
