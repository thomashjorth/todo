using Todo.Host;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Where the host looks for wwwroot, measured on <see cref="TodoHost.Build"/> rather than on a
/// published exe. The published-exe half of the claim - that a real publish finds its real
/// wwwroot - cannot be asserted here: a publish takes some 40 seconds and belongs in
/// scripts\publish.ps1. These two can, and they pull in opposite directions, which is the whole
/// reason both exist.
/// <para>
/// The default is the folder the binary lives in, because the framework's own default is the
/// process working directory and a published exe does not choose that - whoever launches it does.
/// The override must keep winning, because every test host passes <c>--contentRoot</c> to point at
/// src\Todo.Host, and the test binary's folder has no wwwroot at all. Set the content root
/// unconditionally and the second assertion falls; leave it unset and the first one does.
/// </para>
/// </summary>
public class HostContentRootTests
{
    /// <summary>
    /// Compared as it is written rather than as a directory, and that spelling is the assertion's
    /// only teeth. Under <c>dotnet test</c> the process working directory <b>is</b> the test
    /// binary's folder, so the framework's default and this fix name the same folder and a
    /// normalising comparison cannot tell them apart. What can: <c>AppContext.BaseDirectory</c>
    /// ends in a separator and <c>Directory.GetCurrentDirectory()</c> does not, and a rooted
    /// content root is passed through untouched. Measured both ways with the fix removed - the
    /// exact comparison failed on <c>…\net10.0\</c> against <c>…\net10.0</c>, the normalising one
    /// passed. Should a future runtime start normalising that away, this goes red while the code
    /// is right; the answer then is a working directory the test controls, not a looser compare.
    /// </summary>
    [Fact]
    public async Task The_content_root_is_the_folder_the_binary_lives_in_when_nobody_says_otherwise()
    {
        var contentRoot = await ContentRootWhenBuiltWithAsync();

        Assert.Equal(AppContext.BaseDirectory, contentRoot);

        // Says out loud why the override below has to keep working: this folder is not one the
        // app could serve from. A day where the test project starts copying wwwroot beside its
        // own binary is a day this pair of tests stops meaning what it says.
        Assert.False(
            Directory.Exists(Path.Combine(contentRoot, "wwwroot")),
            $"The test binary's folder {contentRoot} has a wwwroot, so this test can no longer "
                + "tell the default apart from a usable content root.");
    }

    [Fact]
    public async Task An_explicit_content_root_still_wins()
    {
        // Without this the test could pass on the default it is supposed to be overriding.
        Assert.NotEqual(Comparable(AppContext.BaseDirectory), Comparable(RepoPaths.HostContentRoot));

        var contentRoot = await ContentRootWhenBuiltWithAsync("--contentRoot", RepoPaths.HostContentRoot);

        Assert.Equal(Comparable(RepoPaths.HostContentRoot), Comparable(contentRoot));
    }

    /// <summary>
    /// Builds the real host against a throwaway database and answers where it decided its content
    /// root is. The database path is never left to the default: that one is the user's own tasks.
    /// </summary>
    private static async Task<string> ContentRootWhenBuiltWithAsync(params string[] extraArgs)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), "TodoApp.Tests", $"{Guid.NewGuid():N}.db");

        try
        {
            await using var app = TodoHost.Build(["--Data:Path", databasePath, .. extraArgs]);

            return app.Environment.ContentRootPath;
        }
        finally
        {
            RunningHost.ClearConnectionPoolFor(databasePath);

            foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>
    /// A rooted content root is passed through as written, trailing separator and all, so the two
    /// spellings of the same folder have to be levelled before they are compared.
    /// </summary>
    private static string Comparable(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
