using Todo.Core.Auth;

namespace Todo.Core.Tests.Auth;

public class PasswordHashTests
{
    // One iteration, as the tests' own host uses: the claims here are about the format and the
    // comparison, and 600,000 rounds per case would only make the suite slow.
    private const int Fast = 1;

    [Fact]
    public void The_password_it_was_made_from_verifies()
    {
        var stored = PasswordHash.Create("hemmelig", Fast);

        Assert.True(PasswordHash.Verify("hemmelig", stored));
    }

    [Fact]
    public void Another_password_does_not_verify()
    {
        var stored = PasswordHash.Create("hemmelig", Fast);

        Assert.False(PasswordHash.Verify("Hemmelig", stored));
    }

    // The browser sends the password as UTF-8, so a hash made of anything else would refuse the right
    // password the day it holds a Danish letter. A round trip cannot see that - Create and Verify
    // would agree on the wrong encoding - so the hash is held against PBKDF2 over the UTF-8 bytes,
    // computed here by hand.
    [Fact]
    public void A_password_with_danish_letters_is_hashed_from_its_utf8_bytes()
    {
        var stored = PasswordHash.Create("blåbærgrød", Fast);
        var parts = stored.Split('$');

        var expected = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes("blåbærgrød"),
            Convert.FromBase64String(parts[2]),
            Fast,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);

        Assert.Equal(Convert.ToBase64String(expected), parts[3]);
        Assert.True(PasswordHash.Verify("blåbærgrød", stored));
    }

    [Fact]
    public void The_same_password_hashes_differently_each_time()
    {
        Assert.NotEqual(PasswordHash.Create("hemmelig", Fast), PasswordHash.Create("hemmelig", Fast));
    }

    // The iteration count travels in the string, so a hash made with one count verifies under any
    // default - which is what lets the tests use one and the app 600,000.
    [Fact]
    public void The_iterations_are_read_from_the_stored_hash()
    {
        var stored = PasswordHash.Create("hemmelig", 3);

        Assert.StartsWith("pbkdf2-sha256$3$", stored);
        Assert.True(PasswordHash.Verify("hemmelig", stored));
    }

    [Fact]
    public void The_default_is_the_slow_one()
    {
        Assert.StartsWith($"pbkdf2-sha256${PasswordHash.DefaultIterations}$", PasswordHash.Create("x"));
        Assert.Equal(600_000, PasswordHash.DefaultIterations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hemmelig")]
    [InlineData("sha1$1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$mange$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$-1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1$ikke base64$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1$AAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("pbkdf2-sha256$1$AAAAAAAAAAAAAAAAAAAAAA==")]
    public void A_damaged_hash_is_not_well_formed_and_verifies_nothing(string stored)
    {
        Assert.False(PasswordHash.IsWellFormed(stored));
        Assert.False(PasswordHash.Verify("hemmelig", stored));
    }

    [Fact]
    public void A_hash_it_made_is_well_formed()
    {
        Assert.True(PasswordHash.IsWellFormed(PasswordHash.Create("hemmelig", Fast)));
    }
}
