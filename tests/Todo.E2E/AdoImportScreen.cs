using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// The Azure DevOps import screen. Shaped like <see cref="JiraImportScreen"/>, and the three places it
/// is not are the slice's own answer to whether the pattern generalised: a row here carries its work
/// item <em>type</em>, and it carries either a proposed deadline or a line saying there is none —
/// neither of which any Jira row has, because Jira's due date came off the issue while this one is the
/// app's own arithmetic.
/// </summary>
public sealed class AdoImportScreen(TodoApp app)
{
    /// <summary>
    /// The screen's own heading. Level 2 rather than its text, because the text is localized and the
    /// h1 above it belongs to the shell — the level is what makes this one unambiguous.
    /// </summary>
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    /// <summary>
    /// Says there is nothing to fetch yet. Unlike Jira's, this needs three things stored rather than
    /// two: Azure DevOps scopes a query by URL path, so a project is as necessary as the collection
    /// and the token.
    /// </summary>
    public ILocator NotConfigured => Page.GetByTestId("ado-not-configured");

    public ILocator SettingsLink => Page.GetByTestId("ado-settings-link");

    /// <summary>
    /// Says the deadline on every row is the app's own suggestion rather than something fetched. It
    /// has no Jira counterpart at all: Azure DevOps has no due date field, measured 2026-08-20.
    /// </summary>
    public ILocator DeadlineNotice => Page.GetByTestId("ado-deadline-notice");

    public ILocator PreviewButton => Page.GetByTestId("ado-preview");

    public ILocator Rows => Page.GetByTestId("ado-row");

    public ILocator Showing => Page.GetByTestId("ado-showing");

    /// <summary>Why every row on screen is untickable, which is not the same as an empty answer.</summary>
    public ILocator NothingToSelect => Page.GetByTestId("ado-nothing-to-select");

    public ILocator NoneAssigned => Page.GetByTestId("ado-none-assigned");

    public ILocator Error => Page.GetByTestId("ado-import-error");

    public ILocator ImportButton => Page.GetByTestId("ado-import");

    public ILocator Receipt => Page.GetByTestId("ado-receipt");

    private IPage Page => app.Page;

    public ILocator Row(string text) => Rows.Filter(new() { HasText = text });

    /// <summary>
    /// The work item type on a row. New against Jira, and deliberately not a branch: the type is
    /// always filled in, because the query matched on it and the batch asked for it.
    /// </summary>
    public static ILocator TypeIn(ILocator row) => row.GetByTestId("ado-type");

    /// <summary>
    /// The deadline the import would set. One half of a branch no Jira screen has — the other half is
    /// <see cref="NoDeadlineIn"/>, and both need a fixture to render them.
    /// </summary>
    public static ILocator DeadlineIn(ILocator row) => row.GetByTestId("ado-deadline");

    public static ILocator NoDeadlineIn(ILocator row) => row.GetByTestId("ado-no-deadline");

    public static ILocator RequesterIn(ILocator row) => row.GetByTestId("ado-requester");

    /// <summary>
    /// Says a description came along, rather than showing it. Azure DevOps' field is raw HTML and not
    /// CommonMark yet, so the markup on screen would be neither what the user ends up reading in the
    /// note nor an honest rendering of it.
    /// </summary>
    public static ILocator NoteIn(ILocator row) => row.GetByTestId("ado-note");

    public static ILocator WaitingIn(ILocator row) => row.GetByTestId("ado-waiting");

    /// <summary>
    /// Since when the work item has been waiting, from Microsoft.VSTS.Common.StateChangeDate. Nested
    /// inside the waiting branch, because a date only comes with a waiting row.
    /// </summary>
    public static ILocator WaitingSinceIn(ILocator row) => row.GetByTestId("ado-waiting-since");

    /// <summary>
    /// Why a single row is being skipped. Scoped to the row rather than the page: the two reasons
    /// carry the same class and only differ in which row they sit on.
    /// </summary>
    public static ILocator ExcludedIn(ILocator row) => row.GetByTestId("ado-excluded");

    public static ILocator AlreadyImportedIn(ILocator row) => row.GetByTestId("ado-already-imported");

    /// <summary>
    /// The button that opens a previewed work item in the system's browser. Scoped to the row, because
    /// every row has one — the contract makes <c>url</c> required, so no branch can leave it out. It is
    /// a &lt;button&gt; rather than an &lt;a href&gt;: the Photino window has neither an address bar
    /// nor a back button. It sits <em>outside</em> the row's &lt;label&gt;, which is what keeps the
    /// checkbox from being announced as "… Åbn sagen".
    /// </summary>
    public static ILocator OpenItemIn(ILocator row) => row.GetByTestId("ado-open-item");

    /// <summary>
    /// Why this row's work item could not be opened. Beside the button that was pressed rather than at
    /// the top of the screen: the column is 480 px wide and a notice above twenty rows is out of sight,
    /// which is the same silence as no notice at all.
    /// </summary>
    public static ILocator OpenErrorIn(ILocator row) => row.GetByTestId("ado-open-error");

    public static ILocator PickOf(ILocator row) => row.Locator("input[type=checkbox]");

    public Task PreviewAsync() => PreviewButton.ClickAsync();

    public Task ImportAsync() => ImportButton.ClickAsync();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Heading).ToBeVisibleAsync();
}
