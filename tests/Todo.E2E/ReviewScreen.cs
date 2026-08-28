using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// The weekly review, whose five groups fold. At most one is open at a time, and the first group
/// holding anything opens itself on arrival — unlike the settings page, which arrives with nothing
/// open. Either way a locator below a closed heading finds nothing: the panel is removed from the
/// DOM, not hidden in it.
/// </summary>
public sealed class ReviewScreen(TodoApp app)
{
    /// <summary>The five groups, spelled the way each heading button's test id is built.</summary>
    public const string OverdueGroup = "overdue";

    public const string WaitingGroup = "waiting";

    public const string StartingGroup = "startingThisWeek";

    public const string NoDeadlineGroup = "noDeadline";

    public const string SomedayGroup = "someday";

    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    public ILocator Complete => Page.GetByTestId("review-complete");

    public ILocator LastCompleted => Page.GetByTestId("review-last");

    public ILocator Never => Page.GetByTestId("review-never");

    public ILocator Empty => Page.GetByTestId("review-empty");

    private IPage Page => app.Page;

    public ILocator GroupToggle(string group) => Page.GetByTestId($"{group}-review-toggle");

    public ILocator Group(string group) => Page.GetByTestId($"{group}-review-group");

    /// <summary>The rows inside one group. Empty while the group is folded, by design.</summary>
    public ILocator RowsIn(string group) => Group(group).GetByTestId("task-row");

    /// <summary>
    /// Unfolds a group, or leaves it open if it already is. Idempotent, so a caller does not have to
    /// know whether this group is the one that opened itself on arrival.
    /// </summary>
    public async Task OpenGroupAsync(string group)
    {
        var toggle = GroupToggle(group);

        if (await toggle.GetAttributeAsync("aria-expanded") != "true")
        {
            await toggle.ClickAsync();
        }

        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
    }

    /// <summary>
    /// Waits on a heading button rather than on a row: the five headings are there whatever the
    /// fixture holds, so this says the screen arrived without also asserting what is on it.
    /// </summary>
    public Task WaitUntilShownAsync()
        => Assertions.Expect(GroupToggle(OverdueGroup)).ToBeVisibleAsync();
}
