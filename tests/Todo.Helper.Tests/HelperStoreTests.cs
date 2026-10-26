namespace Todo.Helper.Tests;

public class HelperStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("TodoHelper.Tests.").FullName;

    private string FilePath => Path.Combine(_directory, "helper.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_missing_file_reads_as_nothing_set_up()
    {
        var settings = new HelperStore(FilePath).Current;

        Assert.Null(settings.ServerUrl);
        Assert.Null(settings.Jira.Token);
        Assert.Equal(HelperSettings.DefaultOllamaUrl, settings.OllamaUrl);
    }

    /// <summary>
    /// The tokens and the key are on disk only as DPAPI blobs: searched for as text, none of the three
    /// is there, while the URLs - which are not secret - are. Read back by a fresh store, all of it.
    /// </summary>
    [Fact]
    public void The_secrets_never_reach_the_file_as_text_and_come_back_whole()
    {
        new HelperStore(FilePath).Update(_ => new HelperSettings
        {
            ServerUrl = "http://nas:8080",
            HelperKey = "the-helpers-key",
            Jira = new SourceSettings { BaseUrl = "https://jira.example", Token = "a-jira-pat" },
            Ado = new SourceSettings { BaseUrl = "https://ado.example/Coll", Token = "an-ado-pat" },
        });

        var file = File.ReadAllText(FilePath);

        Assert.Contains("https://jira.example", file);
        Assert.DoesNotContain("the-helpers-key", file);
        Assert.DoesNotContain("a-jira-pat", file);
        Assert.DoesNotContain("an-ado-pat", file);

        var read = new HelperStore(FilePath).Current;

        Assert.Equal("the-helpers-key", read.HelperKey);
        Assert.Equal("a-jira-pat", read.Jira.Token);
        Assert.Equal("an-ado-pat", read.Ado.Token);
        Assert.Equal("http://nas:8080", read.ServerUrl);
    }

    [Fact]
    public void A_change_is_announced_with_the_new_settings()
    {
        var store = new HelperStore(FilePath);
        HelperSettings? announced = null;
        store.Changed += settings => announced = settings;

        store.Update(s => s with { ServerUrl = "http://nas:8080" });

        Assert.Equal("http://nas:8080", announced?.ServerUrl);
    }
}
