using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class JiraImportScreen(TodoApp app)
{
    /// <summary>
    /// The screen's own heading. Level 2 rather than its text, because the text is localized and
    /// the h1 above it belongs to the shell — the level is what makes this one unambiguous.
    /// </summary>
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    /// <summary>Says there is nothing to fetch yet, which is the state before a token is stored.</summary>
    public ILocator NotConfigured => Page.GetByTestId("jira-not-configured");

    public ILocator SettingsLink => Page.GetByTestId("jira-settings-link");

    /// <summary>
    /// Says the duty rotation is switched on, which is the only place on this screen the state can
    /// be seen. Absent unless the setting is on, so nothing renders it by default.
    /// </summary>
    public ILocator OnDutyNotice => Page.GetByTestId("jira-on-duty-notice");

    public ILocator PreviewButton => Page.GetByTestId("jira-preview");

    public ILocator Rows => Page.GetByTestId("jira-row");

    public ILocator Showing => Page.GetByTestId("jira-showing");

    /// <summary>Why every row on screen is untickable, which is not the same as an empty answer.</summary>
    public ILocator NothingToSelect => Page.GetByTestId("jira-nothing-to-select");

    public ILocator NoneAssigned => Page.GetByTestId("jira-none-assigned");

    /// <summary>
    /// Why the reconciliation had nothing to say. See the twin in <see cref="AdoImportScreen"/>: it
    /// answers for the whole set of reconciled keys and stands in place of their rows, so it is
    /// page-wide rather than scoped to one.
    /// </summary>
    public ILocator ReconciliationNotice => Page.GetByTestId("jira-reconciliation-notice");

    public ILocator Error => Page.GetByTestId("jira-import-error");

    public ILocator ImportButton => Page.GetByTestId("jira-import");

    public ILocator Receipt => Page.GetByTestId("jira-receipt");

    private IPage Page => app.Page;

    public ILocator Row(string text) => Rows.Filter(new() { HasText = text });

    /// <summary>
    /// Why a single row is being skipped. Scoped to the row rather than the page: the two reasons
    /// carry the same class and only differ in which row they sit on.
    /// </summary>
    public static ILocator ExcludedIn(ILocator row) => row.GetByTestId("jira-excluded");

    public static ILocator AlreadyImportedIn(ILocator row)
        => row.GetByTestId("jira-already-imported");

    /// <summary>
    /// The Jira status on a row, and a branch since the reconciliation: a key Jira does not know
    /// brings back no answer to take a status from.
    /// </summary>
    public static ILocator StatusIn(ILocator row) => row.GetByTestId("jira-status");

    /// <summary>
    /// Says the status is missing rather than leaving the line out — "Status: " with nothing after it
    /// cannot be told from a line the screen forgot to fill in. The Azure DevOps twin has two of
    /// these; a Jira row has no type line, so there is only the one.
    /// </summary>
    public static ILocator NoStatusIn(ILocator row) => row.GetByTestId("jira-no-status");

    /// <summary>
    /// The offer to close a task whose issue is finished, or has been handed to somebody else. One
    /// testid with two sentences, so the guard and the journey keep pointing at the same element. It
    /// takes the place of <see cref="AlreadyImportedIn"/> rather than sitting beside it — a row that
    /// offers a closure was necessarily imported before.
    /// </summary>
    public static ILocator SuggestsClosingIn(ILocator row) => row.GetByTestId("jira-suggests-closing");

    /// <summary>
    /// The offer to put a locally finished task back into an open status. Its own testid rather than
    /// sharing the closing one: the two say opposite things, and a locator matching either could not
    /// tell a screen offering the wrong one from a screen offering the right one.
    /// </summary>
    public static ILocator SuggestsReopeningIn(ILocator row)
        => row.GetByTestId("jira-suggests-reopening");

    /// <summary>
    /// Which status the reopening would restore. Its own element rather than a clause inside the
    /// sentence above, because it is the half that can be wrong on its own.
    /// </summary>
    public static ILocator ReopenToIn(ILocator row) => row.GetByTestId("jira-reopen-to");

    /// <summary>
    /// The label saying a row came from the shared duty pool. Scoped to the row, because it is the
    /// row it sits on that carries the meaning — and it renders only when the server put
    /// <c>isDuty</c> on that row, so a preview answer without the field paints no colour here at all.
    /// </summary>
    public static ILocator DutyIn(ILocator row) => row.GetByTestId("jira-duty");

    /// <summary>
    /// The button that opens a previewed issue in the system's browser, so it can be read before
    /// anyone decides to import it. Scoped to the row, because every row has one — the contract makes
    /// <c>url</c> required, so no branch can leave it out, and a page-wide locator would be
    /// ambiguous. It is a &lt;button&gt; rather than an &lt;a href&gt;: the Photino window has
    /// neither an address bar nor a back button.
    /// </summary>
    public static ILocator OpenIssueIn(ILocator row) => row.GetByTestId("jira-open-issue");

    /// <summary>
    /// Why this row's issue could not be opened. Scoped to the row on purpose: the message is held
    /// beside the button that was pressed rather than at the top of the screen, because the column is
    /// 480 px wide and a notice above twenty rows would be out of sight — which is the same silence
    /// as no notice at all. A page-wide locator could not tell the two apart.
    /// </summary>
    public static ILocator OpenErrorIn(ILocator row) => row.GetByTestId("jira-open-error");

    public static ILocator PickOf(ILocator row) => row.Locator("input[type=checkbox]");

    public Task PreviewAsync() => PreviewButton.ClickAsync();

    public Task ImportAsync() => ImportButton.ClickAsync();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Heading).ToBeVisibleAsync();
}
