using Microsoft.Playwright;
using Todo.TestSupport;
using Todo.TestSupport.Builders;

namespace Todo.E2E;

public class MarkdownNoteJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;
    private const string TaskTitle = "Forbered demoen";
    private const string LinkUrl = "https://example.com/dagsorden";
    private const string AddedBullet = "Fjernbetjening";

    // A textarea hands its value back with LF whatever went in, so the literal is normalised
    // here rather than letting this file's CRLF decide whether a comparison holds.
    private static readonly string Note = """
        **Husk** at gennemgå det hele inden vi går på.

        - Lyd
        - Lys

        Dagsordenen ligger [her](https://example.com/dagsorden).

        ```bash
        dotnet test Todo.sln
        ```

        | Deltager | Rolle | Afdeling | Telefon | Ansvar | Sted | Bemærkning |
        | --- | --- | --- | --- | --- | --- | --- |
        | Mette Kirkegaard | Produktejer | Forretningsudvikling | 12 34 56 78 | Styrer slideshowet | København | Kommer ti minutter senere |
        | Rasmus Bjerre | Udvikler | Platformsteamet | 87 65 43 21 | Skriver noter undervejs | Aarhus | Viser den nye import frem |
        """.ReplaceLineEndings("\n");

    private static readonly string EditedNote =
        Note.Replace("- Lys", $"- Lys\n- {AddedBullet}", StringComparison.Ordinal);

    private static readonly string[] BulletsBefore = ["Lyd", "Lys"];

    private static readonly string[] BulletsAfter = ["Lyd", "Lys", AddedBullet];

    [Fact]
    public async Task A_note_is_read_as_markdown_edited_by_clicking_it_and_its_links_open_outside()
    {
        await using var host = await RunningHost.StartAsync();

        await host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled(TaskTitle).WithNote(Note).Build());

        var app = await TodoApp.OpenAsync(
            fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });
        var tasks = app.Tasks;

        await tasks.RowTitled(TaskTitle).ClickAsync();
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        await Assertions.Expect(tasks.NoteRendered.Locator("strong")).ToHaveTextAsync("Husk");
        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(BulletsBefore);
        await Assertions.Expect(tasks.NoteLink).ToHaveAttributeAsync("href", LinkUrl);
        await Assertions.Expect(tasks.NoteRendered.Locator("pre"))
            .ToContainTextAsync("dotnet test Todo.sln");
        await Assertions.Expect(tasks.NoteTable.Locator("th")).ToHaveCountAsync(7);

        await tasks.NoteTable.ScrollIntoViewIfNeededAsync();

        var pageWidth = await app.ClientWidthAsync();
        var scrolledWidth = await app.ScrollWidthAsync();
        var tableWidth = await tasks.NoteTable.EvaluateAsync<int>("table => table.scrollWidth");

        Assert.True(tableWidth > pageWidth,
            $"The table is only {tableWidth}px wide and fits the {pageWidth}px page on its own, "
            + "so it cannot show whether the note keeps a wide table to itself.");
        Assert.True(scrolledWidth <= pageWidth,
            $"The note pushes the page sideways: scrollWidth was {scrolledWidth} against a "
            + $"clientWidth of {pageWidth}.");

        await tasks.NoteRendered.Locator("strong").ClickAsync();

        await Assertions.Expect(tasks.NoteEditor).ToHaveValueAsync(Note);
        await Assertions.Expect(tasks.NoteRendered).ToHaveCountAsync(0);

        await tasks.NoteEditor.FillAsync(EditedNote);
        await tasks.NoteEditor.PressAsync("Escape");

        await Assertions.Expect(tasks.NoteEditor).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(BulletsAfter);

        tasks = await app.ReloadAsync();

        await tasks.RowTitled(TaskTitle).ClickAsync();

        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(BulletsAfter);

        var opened = new TaskCompletionSource<string?>();

        // Letting the request through would ask the operating system to open the link, and a
        // test run has no business putting a browser window on the machine it runs on.
        await app.Page.RouteAsync("**/api/system/open-link", async route =>
        {
            opened.TrySetResult(route.Request.PostDataJSON()?.GetProperty("url").GetString());
            await route.AbortAsync();
        });

        await app.Page.EvaluateAsync("window.stampedBeforeTheClick = true");

        await tasks.NoteLink.ClickAsync();

        Assert.Equal(LinkUrl, await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assertions.Expect(tasks.NoteEditor).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(BulletsAfter);

        Assert.True(
            await app.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The link took the window with it, and this window has no way back.");
    }
}
