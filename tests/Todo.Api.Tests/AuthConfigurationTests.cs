using Microsoft.Extensions.Configuration;
using Todo.Host;
using Todo.Host.Auth;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The host refuses to start without a password, and refuses before it has touched the database.
/// Built directly rather than through <see cref="RunningHost"/>, which always supplies the test hash.
/// </summary>
public class AuthConfigurationTests
{
    // Empty rather than absent: the command line beats the environment, and on a machine where the
    // user has run SetPassword.cmd, an absent key would be filled in from their own variable.
    [Fact]
    public void A_host_without_a_password_refuses_to_start_and_names_the_cure()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => BuildWith(""));

        Assert.Contains("SetPassword.cmd", refusal.Message);
        Assert.Contains("Auth__PasswordHash", refusal.Message);
    }

    [Fact]
    public void A_host_whose_hash_is_not_one_the_app_made_refuses_to_start()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => BuildWith("hemmelig"));

        Assert.Contains("not a hash this app made", refusal.Message);
    }

    // The order is the claim: Build migrates and takes a backup further down, and a host that is
    // going to refuse must not have done either on the way.
    [Fact]
    public void A_refused_host_leaves_no_database_behind()
    {
        var databasePath = TemporaryDatabase();

        Assert.Throws<InvalidOperationException>(() => BuildWith("", databasePath));

        Assert.False(File.Exists(databasePath), $"The refused host created {databasePath}.");
    }

    // The default only the container relies on: every test host names its own keys path, so without
    // this the folder the NAS keeps its login keys in would be measured by nothing but the probe.
    [Fact]
    public void The_keys_live_beside_the_database_when_no_path_is_named()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "nas-data", "todo.db");
        var configuration = new ConfigurationBuilder().Build();

        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "nas-data", "keys"),
            HostAuth.KeysPath(configuration, databasePath));
    }

    [Fact]
    public void A_named_keys_path_wins_over_the_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [HostAuth.KeysPathKey] = "/elsewhere" })
            .Build();

        Assert.Equal("/elsewhere", HostAuth.KeysPath(configuration, "/data/todo.db"));
    }

    private static void BuildWith(string passwordHash, string? databasePath = null) =>
        TodoHost.Build(
        [
            "--Data:Path", databasePath ?? TemporaryDatabase(),
            "--Auth:PasswordHash", passwordHash,
        ]);

    private static string TemporaryDatabase() =>
        Path.Combine(Path.GetTempPath(), "TodoApp.Tests", $"{Guid.NewGuid():N}.db");
}
