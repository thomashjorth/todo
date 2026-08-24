using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// Every action has to be reachable without a mouse. The first journey walks one task from created
/// to expanded to deleted using only the keyboard; the rest cover the seven Alt shortcuts and the
/// AltGr combination the app has to keep its hands off.
/// </summary>
public class KeyboardJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string Title = "Send referatet";
    private const string CompletedTitle = "Ryd skrivebordet";

    /// <summary>A second open task, so the search has something to leave out.</summary>
    private const string OtherTitle = "Book mødelokalet";
    private const string SomedayTitle = "Læs om typografi";

    /// <summary>
    /// Five in the nav, one in the new-task field, one in the search field and one per switch —
    /// every shortcut the app has. Grew from six with the Jira import screen in slice 11, which
    /// added Alt+J to the nav, to eight with the Azure DevOps import screen in slice 12, which
    /// added Alt+A, and to nine with the search field, which added Alt+K.
    /// </summary>
    private const int BadgeCount = 9;

    /// <summary>The nine digits a row can claim. Alt+0 is not a tenth row, it is a key nobody would guess.</summary>
    private const int RowDigits = 9;

    /// <summary>
    /// D, S, O, N, T, U and L — the seven field shortcuts every open panel has, whatever the task's
    /// state is.
    /// </summary>
    private const int PanelFieldShortcuts = 7;

    /// <summary>
    /// V, the who field, which only exists behind <c>@if (task().status === waitingFor)</c>. Its own
    /// name rather than an eight above, so the sister assertion below says out loud that it is
    /// counting a branch the fixture had to put a task in.
    /// </summary>
    private const int WaitingOnFieldShortcut = 1;

    /// <summary>A task that waits on somebody, so the panel's who field is rendered.</summary>
    private const string WaitingTitle = "Afventer godkendelse";

    /// <summary>
    /// The five nav links in app.html are the first focusable elements on the page, so the
    /// field is the sixth stop — not the first. Asserted below rather than assumed: another
    /// link, or a skip link, has to fail here with the name of what appeared, not somewhere
    /// later where the failure would read as a broken field.
    /// </summary>
    private static readonly string[] TrailToTheField =
        ["nav-tasks", "nav-import", "nav-jira", "nav-ado", "nav-settings", "new-task-input"];

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_task_can_be_created_expanded_and_deleted_without_a_mouse()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        // Create: focus the field by tabbing, not by clicking.
        var trail = new List<string>();
        for (var i = 0; i < TrailToTheField.Length; i++)
        {
            await App.Page.Keyboard.PressAsync("Tab");
            trail.Add(await FocusedTestIdAsync());
        }

        Assert.Equal(TrailToTheField, trail);

        await App.Page.Keyboard.TypeAsync(Title);
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeVisibleAsync();

        // Expand: the row title is a button, so Enter on it must open the detail panel.
        await tasks.RowTitled(Title).FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        // The note's edit button is the keyboard path to editing — the click handlers on the
        // rendered note are a mouse shortcut, not the only way in.
        await tasks.Detail.GetByTestId("note-edit").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.NoteEditor).ToBeVisibleAsync();

        // The editor takes focus on its own, so Escape reaches it without a click. Checked
        // rather than assumed: Escape pressed at the page would close nothing and leave the
        // editor open, and the delete below would still pass.
        Assert.Equal("note-editor", await FocusedTestIdAsync());

        await App.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tasks.NoteEditor).ToBeHiddenAsync();

        // Delete: reachable and activatable by keyboard.
        await tasks.Detail.GetByTestId("delete-task").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeHiddenAsync();
    }

    /// <summary>
    /// Alt on its own only paints the badges. Counted, not read: the switch beside the V badge is
    /// labelled "Vis færdige", so a test that looked for the letter V in the page text would find
    /// one whether a badge rendered or not — and would fail even when the badges were hidden.
    /// </summary>
    [Fact]
    public async Task Holding_Alt_reveals_the_badges_and_releasing_it_hides_them_again()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        // Zero first. Without this the last count could be met by a page that never had a badge,
        // and the whole test would be about nothing.
        await Assertions.Expect(Badges).ToHaveCountAsync(0);

        await App.Page.Keyboard.DownAsync("Alt");
        await Assertions.Expect(Badges).ToHaveCountAsync(BadgeCount);

        await App.Page.Keyboard.UpAsync("Alt");
        await Assertions.Expect(Badges).ToHaveCountAsync(0);
    }

    /// <summary>A text field has no activation beyond taking focus, so Alt+N only focuses.</summary>
    [Fact]
    public async Task Alt_N_focuses_the_new_task_field()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.Keyboard.PressAsync("Alt+n");

        Assert.Equal("new-task-input", await FocusedTestIdAsync());
    }

    /// <summary>
    /// Same as Alt+N, and K rather than a letter from the Danish word: Ctrl/Cmd+K is the search
    /// shortcut people arrive with, and S was already the settings link. Chrome on Windows binds no
    /// Alt+K, and the near neighbour is Ctrl+K - the address bar - which is a different modifier set.
    /// <para>
    /// Typing after the press is what says the field really has focus rather than merely looking as
    /// though it does: the list has to narrow without the mouse touching anything.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Alt_K_focuses_the_search_field_and_typing_narrows_the_list()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(Title).DueToday().Build(),
            new TaskItemBuilder(Clock).Titled(OtherTitle).DueToday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(2);

        await App.Page.Keyboard.PressAsync("Alt+k");

        Assert.Equal("task-search", await FocusedTestIdAsync());

        // Typed rather than filled, because typing is what a focused field receives - a Fill would
        // have worked whether the press moved focus or not.
        await App.Page.Keyboard.TypeAsync("referat");

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.Rows.First).ToContainTextAsync(Title);
    }

    /// <summary>
    /// Windows' access-key convention on a checkbox: it toggles, and focus lands there too. The
    /// completed section arriving is what says the (change) handler ran and the store reloaded —
    /// a checkbox that only took focus would leave the section absent.
    /// </summary>
    [Fact]
    public async Task Alt_V_toggles_the_completed_switch_and_takes_focus()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(0);

        await App.Page.Keyboard.PressAsync("Alt+v");

        // A completed row is a checkbox and a span with no button at all, so its text is the
        // assertion — RowTitled has nothing to match on in that section.
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);
        await Assertions.Expect(tasks.ShowCompleted).ToBeCheckedAsync();

        Assert.Equal("show-completed", await FocusedTestIdAsync());
    }

    [Fact]
    public async Task Alt_M_toggles_the_someday_switch_and_takes_focus()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(SomedayTitle).Someday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.SomedayRows).ToHaveCountAsync(0);

        await App.Page.Keyboard.PressAsync("Alt+m");

        await Assertions.Expect(tasks.SomedayRows).ToContainTextAsync(SomedayTitle);
        await Assertions.Expect(tasks.ShowSomeday).ToBeCheckedAsync();

        Assert.Equal("show-someday", await FocusedTestIdAsync());
    }

    /// <summary>
    /// A link's access key follows the link. Asserted on the destination screen rather than on the
    /// link having focus: focus is what the app did before this convention was fixed, and it left
    /// the user pressing Enter afterwards.
    /// </summary>
    [Fact]
    public async Task Alt_I_follows_the_import_link()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.Keyboard.PressAsync("Alt+i");

        await Assertions.Expect(new RetroImportScreen(App).Csv).ToBeVisibleAsync();
        await Assertions.Expect(App.Tasks.NewTaskInput).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Alt+J was the fourth nav link's shortcut from the day it arrived, but only the badge count
    /// covered it — and a badge is painted by the Alt keydown alone, whether or not the letter does
    /// anything. The destination is asserted through a locator that cannot exist on the task list,
    /// so a shortcut that only moved focus fails here.
    /// </summary>
    [Fact]
    public async Task Alt_J_follows_the_jira_link()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.Keyboard.PressAsync("Alt+j");

        await Assertions.Expect(new JiraImportScreen(App).NotConfigured).ToBeVisibleAsync();
        await Assertions.Expect(App.Tasks.NewTaskInput).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Alt+A, the fifth nav link's shortcut, arriving with the Azure DevOps import screen in slice 12.
    /// Same shape as Alt+J: the destination is asserted through a locator that cannot exist on the task
    /// list, so a shortcut that only moved focus fails here.
    /// </summary>
    [Fact]
    public async Task Alt_A_follows_the_ado_link()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.Keyboard.PressAsync("Alt+a");

        await Assertions.Expect(new AdoImportScreen(App).NotConfigured).ToBeVisibleAsync();
        await Assertions.Expect(App.Tasks.NewTaskInput).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Nothing else in the repo can see a shortcut collision. <c>ShortcutStore.register</c> is a plain
    /// <c>Map.set</c>, so a second claim on a letter overwrites the first in silence; the badge count
    /// cannot see it, because a badge is one element per directive whatever letter it carries; and the
    /// visible badge text is written in the template rather than derived from <c>appShortcut</c>, so it
    /// would keep showing the old letter. Measured in slice 12: setting nav-ado to <c>j</c> failed none
    /// of the 239 Vitest specs.
    ///
    /// The letters are read off <c>aria-keyshortcuts</c>, which the directive puts on its own host — so
    /// this is a claim about what the directive actually registered rather than about what a template
    /// says. It is also the only assertion anywhere on that attribute, which the design document's
    /// section 10 lists as unguarded.
    ///
    /// The limit is worth knowing: only shortcuts <em>rendered right now</em> are compared, and all
    /// eight happen to live on the task list today — five nav links, the new-task field and the two
    /// switches. A future shortcut that exists only on another screen would need its own pass, and the
    /// count below is what forces that question rather than letting it slide.
    /// </summary>
    [Fact]
    public async Task Every_shortcut_letter_on_screen_is_its_own()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var claimed = await ClaimedShortcutsAsync();

        // Every shortcut the app has, so this cannot pass on a page that rendered none of them — the
        // same reason the badge count asserts zero before it asserts eight.
        Assert.Equal(BadgeCount, claimed.Length);

        AssertLettersAreDistinct(claimed);
    }

    /// <summary>
    /// The sister of the assertion above, and the reason it can stay as it is: that one seeds no
    /// tasks, so not one of the row digits and not one of the panel's field letters is on the page
    /// it measures — a collision in either new layer cannot fail it. This one seeds a list and opens
    /// the panel, so all three layers are rendered at once, which is the only state where they can
    /// collide.
    /// <para>
    /// The count is worked out from the fixture rather than written down. A constant that has to be
    /// recomputed for every change to the seeded list is a guard that gets switched off the first
    /// time it is in the way — and the fixture here is deliberately one task short of nine, so the
    /// <c>Math.Min</c> is doing arithmetic rather than restating a limit.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_shortcut_letter_on_a_seeded_list_with_the_panel_open_is_its_own()
    {
        // Distinct deadlines, so the order on screen is the server's and not a coincidence.
        string[] scheduled = ["Alfa-opgaven", "Bravo-opgaven", "Charlie-opgaven"];

        await Host.AddAndSaveChangesAsync(
            [
                ..scheduled.Select((title, i) => new TaskItemBuilder(Clock)
                    .Titled(title)
                    .DueOn(Clock.Today.AddDays(i + 1))
                    .Build()),
                new TaskItemBuilder(Clock).Titled(WaitingTitle).WaitingFor("Flemming").Build(),
            ]);

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1400 });
        var tasks = App.Tasks;

        // The waiting task, so the panel's V is rendered: the who field only exists behind
        // @if (task().status === waitingFor), and an unrendered directive registers nothing.
        await tasks.RowShowing(WaitingTitle).ClickAsync();
        await Assertions.Expect(tasks.WaitingOnInput).ToBeVisibleAsync();

        var selectable = scheduled.Length + 1;
        var expected = BadgeCount
            + Math.Min(RowDigits, selectable)
            + PanelFieldShortcuts
            + WaitingOnFieldShortcut;

        var claimed = await ClaimedShortcutsAsync();

        Assert.Equal(expected, claimed.Length);

        AssertLettersAreDistinct(claimed);
    }

    /// <summary>
    /// The digit picks the n'th row, and <em>three</em> is the assertion's teeth: side by side the
    /// panel auto-selects <c>[0]</c>, so a journey that pressed Alt+1 would pass with the whole
    /// numbering removed. At 480 px nothing is selected until something selects it, and the third
    /// row is a row no other rule would have reached.
    /// </summary>
    [Fact]
    public async Task Alt_3_selects_the_third_row()
    {
        string[] titles = ["Alfa-opgaven", "Bravo-opgaven", "Charlie-opgaven"];

        await Host.AddAndSaveChangesAsync(
            [.. titles.Select((title, i) => new TaskItemBuilder(Clock)
                .Titled(title)
                .DueOn(Clock.Today.AddDays(i + 1))
                .Build())]);

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(titles.Length);
        await Assertions.Expect(tasks.Detail).ToHaveCountAsync(0);

        await App.Page.Keyboard.PressAsync("Alt+3");

        // In one column the panel lives inside its own row, so DetailFor is what tells the third
        // row apart from the first — Detail alone would pass on any row having opened.
        await Assertions.Expect(tasks.DetailFor(titles[2])).ToBeVisibleAsync();
        await Assertions.Expect(tasks.DetailFor(titles[0])).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.Detail).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Alt+0 is not a tenth row, so the tenth row has no shortcut at all — measured rather than
    /// assumed, because the directive's empty-key guard is what makes it so. The ninth row's
    /// attribute is asserted first: without it a page that rendered no attributes anywhere would
    /// satisfy the zero below.
    /// </summary>
    [Fact]
    public async Task The_tenth_row_has_no_shortcut()
    {
        // Eleven, so the tenth is not also the last: a locator that silently matched the end of the
        // list would pass on ten.
        string[] titles =
        [
            "Række A", "Række B", "Række C", "Række D", "Række E", "Række F",
            "Række G", "Række H", "Række I", "Række J", "Række K",
        ];

        await Host.AddAndSaveChangesAsync(
            [.. titles.Select((title, i) => new TaskItemBuilder(Clock)
                .Titled(title)
                .DueOn(Clock.Today.AddDays(i))
                .Build())]);

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 2000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(titles.Length);

        await Assertions.Expect(tasks.RowShortcutFor(titles[0]))
            .ToHaveAttributeAsync("aria-keyshortcuts", "Alt+1");
        await Assertions.Expect(tasks.RowShortcutFor(titles[RowDigits - 1]))
            .ToHaveAttributeAsync("aria-keyshortcuts", $"Alt+{RowDigits}");

        await Assertions.Expect(tasks.RowShortcutFor(titles[RowDigits])).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.RowShortcutFor(titles[RowDigits + 1])).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The field layer, reached with Shift held: Alt+D is nothing and Alt+Shift+D is the deadline.
    /// The row is opened first, because at 480 px there is no panel until there is.
    /// </summary>
    [Fact]
    public async Task Alt_Shift_D_focuses_the_deadline_field()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(Title).DueToday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.RowShowing(Title).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(Title)).ToBeVisibleAsync();

        await App.Page.Keyboard.PressAsync("Alt+Shift+D");

        Assert.Equal("deadline-input", await FocusedTestIdAsync());
    }

    /// <summary>
    /// Delete is the one shortcut in the panel that only takes focus, because the app has neither a
    /// confirmation nor an undo — the second keystroke <em>is</em> the confirmation. The second half
    /// of this journey is the only assertion anywhere that can tell <c>focus</c> from
    /// <c>activate</c>, and it is written with two full round trips in between: "the task is still
    /// there" polls, and the first successful poll ends the wait, so read straight after the press
    /// it would be read before a delete could have removed anything. Toggling the completed switch
    /// on and off again is a GET each way, so a DELETE fired by the press has landed by the time the
    /// last assertion looks.
    /// </summary>
    [Fact]
    public async Task Alt_Shift_L_focuses_the_delete_button_without_deleting()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(Title).DueToday().Build(),
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.RowShowing(Title).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(Title)).ToBeVisibleAsync();

        await App.Page.Keyboard.PressAsync("Alt+Shift+L");

        Assert.Equal("delete-task", await FocusedTestIdAsync());

        await tasks.ShowCompleted.ClickAsync();
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);

        await tasks.ShowCompleted.ClickAsync();
        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(0);

        await Assertions.Expect(tasks.RowShowing(Title)).ToBeVisibleAsync();
        await Assertions.Expect(tasks.DetailFor(Title)).ToBeVisibleAsync();
    }

    /// <summary>
    /// The note's shortcut is the only activating one in the panel, and the caret has to arrive with
    /// it: the editor being visible is the click, and the focus is the detail component's existing
    /// effect carrying the shortcut the rest of the way. Asserting only visibility would leave the
    /// user pressing Tab afterwards.
    /// </summary>
    [Fact]
    public async Task Alt_Shift_N_opens_the_note_editor_and_puts_the_caret_in_it()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(Title).DueToday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.RowShowing(Title).ClickAsync();
        await Assertions.Expect(tasks.NoteEditButton).ToBeVisibleAsync();
        await Assertions.Expect(tasks.NoteEditor).ToHaveCountAsync(0);

        await App.Page.Keyboard.PressAsync("Alt+Shift+N");

        await Assertions.Expect(tasks.NoteEditor).ToBeVisibleAsync();

        Assert.Equal("note-editor", await FocusedTestIdAsync());
    }

    [Fact]
    public async Task Alt_S_follows_the_settings_link()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.Keyboard.PressAsync("Alt+s");

        await Assertions.Expect(new SettingsScreen(App).SectionToggle(SettingsScreen.LanguageSection))
            .ToBeVisibleAsync();
        await Assertions.Expect(App.Tasks.NewTaskInput).ToHaveCountAsync(0);
    }

    /// <summary>
    /// From the settings screen, because the app opens on the task list: pressing Alt+O there and
    /// asserting the list is shown would pass with the shortcut doing nothing whatsoever.
    /// </summary>
    [Fact]
    public async Task Alt_O_follows_the_tasks_link()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var settings = await App.GoToSettings();

        await App.Page.Keyboard.PressAsync("Alt+o");

        await Assertions.Expect(App.Tasks.NewTaskInput).ToBeVisibleAsync();
        // The heading, not the language select: the select is absent on the settings page too, now
        // that the groups arrive folded, so asserting on it could not tell the two screens apart.
        await Assertions.Expect(settings.SectionToggle(SettingsScreen.LanguageSection))
            .ToHaveCountAsync(0);
    }

    /// <summary>
    /// Ctrl+Alt is AltGr on a Danish keyboard, so the app must leave it alone or the user cannot
    /// type @, £ or $. The plain Alt+N afterwards is the control: without it this would pass just
    /// as well on an app where the shortcut had been deleted.
    /// </summary>
    [Fact]
    public async Task Ctrl_Alt_N_is_left_alone_because_it_is_AltGr()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Page.GetByTestId("nav-tasks").FocusAsync();
        await App.Page.Keyboard.PressAsync("Control+Alt+n");

        Assert.Equal("nav-tasks", await FocusedTestIdAsync());

        await App.Page.Keyboard.PressAsync("Alt+n");

        Assert.Equal("new-task-input", await FocusedTestIdAsync());
    }

    private ILocator Badges => App.Page.GetByTestId("shortcut-badge");

    /// <summary>
    /// Every shortcut rendered right now, read off <c>aria-keyshortcuts</c> — so this is what the
    /// directive registered rather than what a template says.
    /// </summary>
    private Task<string[]> ClaimedShortcutsAsync() => App.Page.EvaluateAsync<string[]>(
        """
        () => [...document.querySelectorAll('[aria-keyshortcuts]')].map(
          (el) => `${el.dataset.testid ?? el.tagName.toLowerCase()}=${el.getAttribute('aria-keyshortcuts')}`)
        """);

    /// <summary>
    /// The whole list is in the message rather than the duplicate alone: a collision is two
    /// elements' business, and the one that lost is the one nobody would think to look at.
    /// </summary>
    private static void AssertLettersAreDistinct(string[] claimed)
    {
        var letters = claimed
            .Select(entry => entry[(entry.IndexOf('=') + 1)..])
            .ToList();

        Assert.True(letters.Distinct(StringComparer.Ordinal).Count() == letters.Count,
            "Two elements claim the same Alt letter, and the last one to register wins in silence: "
            + string.Join(", ", claimed.Order()));
    }

    private Task<string> FocusedTestIdAsync() => App.Page.EvaluateAsync<string>(
        "() => document.activeElement?.dataset.testid ?? 'none'");
}
