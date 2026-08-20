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
    private const string SomedayTitle = "Læs om typografi";

    /// <summary>
    /// Five in the nav, one in the new-task field and one per switch — every shortcut the app has.
    /// Grew from six with the Jira import screen in slice 11, which added Alt+J to the nav, and to
    /// eight with the Azure DevOps import screen in slice 12, which added Alt+A.
    /// </summary>
    private const int BadgeCount = 8;

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

        var claimed = await App.Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('[aria-keyshortcuts]')].map(
              (el) => `${el.dataset.testid ?? el.tagName.toLowerCase()}=${el.getAttribute('aria-keyshortcuts')}`)
            """);

        // Every shortcut the app has, so this cannot pass on a page that rendered none of them — the
        // same reason the badge count asserts zero before it asserts eight.
        Assert.Equal(BadgeCount, claimed.Length);

        var letters = claimed
            .Select(entry => entry[(entry.IndexOf('=') + 1)..])
            .ToList();

        // The whole list is in the message rather than the duplicate alone: a collision is two
        // elements' business, and the one that lost is the one nobody would think to look at.
        Assert.True(letters.Distinct(StringComparer.Ordinal).Count() == letters.Count,
            "Two elements claim the same Alt letter, and the last one to register wins in silence: "
            + string.Join(", ", claimed.Order()));
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

    private Task<string> FocusedTestIdAsync() => App.Page.EvaluateAsync<string>(
        "() => document.activeElement?.dataset.testid ?? 'none'");
}
