using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// The settings page folds into five groups and shows at most one of them. Measured in the browser
/// rather than in Vitest for the one thing only a browser decides: the heading button's accessible
/// name. The chevron is a text node inside the button, so without <c>aria-hidden</c> it joins that
/// name — and the whole suite matches accessible names in full.
/// </summary>
public class SettingsAccordionJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    /// <summary>
    /// The five headings, in the order the page shows them, spelled as the user reads them. Paired
    /// with the section name so a failure names the group rather than an index.
    /// </summary>
    private static readonly (string Section, string Heading)[] Groups =
    [
        // "Generelt", not "Sprog", since slice 16 put autostart in this group.
        (SettingsScreen.LanguageSection, "Generelt"),
        (SettingsScreen.DelegateSection, "Uddelegering"),
        (SettingsScreen.JiraSection, "Jira-import"),
        (SettingsScreen.AdoSection, "ADO-import"),
        (SettingsScreen.RetroSection, "Retro-import"),
    ];

    [Fact]
    public async Task Only_the_group_you_click_is_open_and_clicking_it_again_folds_it()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var settings = await App.GoToSettings();

        // Nothing open on arrival, which is the point of the fold: five headings and no fields.
        foreach (var (section, heading) in Groups)
        {
            await Assertions.Expect(settings.SectionToggle(section))
                .ToHaveAttributeAsync("aria-expanded", "false");
            await Assertions.Expect(settings.SectionPanel(section)).ToHaveCountAsync(0);

            // Exact, and the assertion the chevron's aria-hidden is measured by: the arrow is a
            // text node in the same button, so losing the attribute makes this name "Sprog ▸".
            await Assertions.Expect(
                    App.Page.GetByRole(AriaRole.Button, new() { Name = heading, Exact = true }))
                .ToHaveCountAsync(1);
        }

        await Assertions.Expect(settings.Language).ToHaveCountAsync(0);
        await Assertions.Expect(settings.JiraBaseUrl).ToHaveCountAsync(0);

        // One open, and only one.
        await settings.OpenAsync(SettingsScreen.JiraSection);

        await Assertions.Expect(settings.JiraBaseUrl).ToBeVisibleAsync();
        await AssertOnlyOpenAsync(settings, SettingsScreen.JiraSection);

        // The second click is what proves the rule rather than a page that happens to start folded:
        // opening another group has to fold this one, fields and all.
        await settings.OpenAsync(SettingsScreen.AdoSection);

        await Assertions.Expect(settings.AdoBaseUrl).ToBeVisibleAsync();
        await Assertions.Expect(settings.JiraBaseUrl).ToHaveCountAsync(0);
        await AssertOnlyOpenAsync(settings, SettingsScreen.AdoSection);

        // And clicking the open one folds it: nothing open is a state a click can reach, not only
        // the state the page arrived in.
        await settings.SectionToggle(SettingsScreen.AdoSection).ClickAsync();

        await Assertions.Expect(settings.AdoBaseUrl).ToHaveCountAsync(0);
        foreach (var (section, _) in Groups)
        {
            await Assertions.Expect(settings.SectionToggle(section))
                .ToHaveAttributeAsync("aria-expanded", "false");
            await Assertions.Expect(settings.SectionPanel(section)).ToHaveCountAsync(0);
        }

        // A heading row is a label and a chevron pushed apart, which is the shape that overflows a
        // narrow column. Compared with clientWidth rather than with 480, because a vertical
        // scrollbar makes the client width 465 and a fixed expectation would fail for that instead.
        await settings.OpenAsync(SettingsScreen.AdoSection);

        var pageWidth = await App.ClientWidthAsync();
        var scrolledWidth = await App.ScrollWidthAsync();

        Assert.True(scrolledWidth <= pageWidth,
            $"The folded settings page overflows sideways: scrollWidth was {scrolledWidth} against "
            + $"a clientWidth of {pageWidth}.");
    }

    /// <summary>
    /// One group expanded and the other four folded, asserted on every one of the five. A check that
    /// only counted the open ones would pass on none open at all, which is where the page starts.
    /// </summary>
    private static async Task AssertOnlyOpenAsync(SettingsScreen settings, string open)
    {
        foreach (var (section, _) in Groups)
        {
            var expanded = section == open ? "true" : "false";

            await Assertions.Expect(settings.SectionToggle(section))
                .ToHaveAttributeAsync("aria-expanded", expanded);
            await Assertions.Expect(settings.SectionPanel(section))
                .ToHaveCountAsync(section == open ? 1 : 0);
        }
    }
}
