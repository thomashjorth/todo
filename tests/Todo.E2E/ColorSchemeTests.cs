using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// <para>
/// The browser paints surfaces the page never gets to style: a <c>&lt;select&gt;</c>'s dropdown
/// popup, the scrollbars, the canvas behind the document. They follow <c>color-scheme</c>, and
/// <see cref="ContrastTests"/> cannot see any of them — it measures DOM nodes, and none of these is
/// one.
/// </para>
/// <para>
/// This is that blind spot's guard, and it holds two assertions because the blind spot hid two
/// separate defects — a distinction worth keeping, because conflating them cost two failed fixes.
/// </para>
/// <para>
/// The first: the declaration sat on <c>&lt;body&gt;</c>, where it styles the body and everything
/// under it but leaves the root at <c>color-scheme: normal</c>. Measured 2026-08-27 with the
/// preference on dark, the root's <c>Canvas</c> was <c>rgb(255, 255, 255)</c> while the body's was
/// <c>rgb(18, 18, 18)</c> — a light document with a dark body inside it. That is what the scrollbars
/// and the canvas were following.
/// </para>
/// <para>
/// The second, and the one the user actually reported: the white dropdown was <em>not</em> caused by
/// that. Moving the declaration to the root made the root's used scheme dark, and the popup stayed
/// white; so did writing <c>color-scheme: dark</c> out explicitly. The dropdown turned out to be
/// painted from the select's own <c>background-color</c>, which no rule had ever set. Do not fold
/// the two back together: <c>color-scheme</c> does not reach a <c>&lt;select&gt;</c> popup in
/// WebView2, measured twice.
/// </para>
/// <para>
/// The assertion is on the <em>used</em> scheme rather than on the markup, and that is deliberate:
/// it says what the browser concluded, not where a class was written, so moving the declaration to a
/// <c>&lt;meta name="color-scheme"&gt;</c> would keep it honest. It is read through the system colour
/// <c>Canvas</c>, which resolves against the used scheme of the element it is asked on.
/// </para>
/// </summary>
public class ColorSchemeTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    /// <summary>Chromium's dark canvas is near-black and its light one white, so the midpoint is
    /// nowhere near either and the threshold cannot be the thing that decides the outcome. Pinning
    /// the exact bytes would make a Chromium tweak look like a regression.</summary>
    private const int DarkBelow = 128;

    [Theory]
    [InlineData(ColorScheme.Light, false)]
    [InlineData(ColorScheme.Dark, true)]
    public async Task The_root_element_follows_the_theme_so_the_browsers_own_surfaces_do_too(
        ColorScheme scheme, bool expectDark)
    {
        await OpenAppAsync(new() { Width = 1400, Height = 900 }, scheme);

        // Asked first, so a failure below cannot be the emulation quietly not taking: without this
        // the dark row would pass on a light page for the wrong reason.
        var prefersDark = await App.Page.EvaluateAsync<bool>(
            "() => matchMedia('(prefers-color-scheme: dark)').matches");

        Assert.Equal(expectDark, prefersDark);

        // Appended to the root rather than into the body, because the root's scheme is the whole
        // question - a probe under <body> answers a different one, and answers it with a pass.
        var canvas = await App.Page.EvaluateAsync<string>(@"() => {
            const probe = document.createElement('div');
            probe.style.backgroundColor = 'Canvas';
            document.documentElement.appendChild(probe);
            const colour = getComputedStyle(probe).backgroundColor;
            probe.remove();
            return colour;
        }");

        var isDark = Luminance(canvas) < DarkBelow;

        Assert.True(isDark == expectDark,
            $"The root element's used colour scheme does not follow the theme. With "
            + $"prefers-color-scheme: {(expectDark ? "dark" : "light")} the system colour Canvas "
            + $"resolved to {canvas} at the root, which reads as "
            + $"{(isDark ? "dark" : "light")}. The browser paints the select popup, the scrollbars "
            + $"and the canvas from this, so declare color-scheme on the root element - on <body> it "
            + $"reaches the page and not the browser's own surfaces.");
    }

    /// <summary>
    /// <para>
    /// The status select's dropdown is painted from the select's and its options' own
    /// <c>background-color</c> and <c>color</c> — not from <c>color-scheme</c>, which is the
    /// measurement that cost two failed fixes. Measured 2026-08-27 in dark theme with the root
    /// already resolving dark: both the select and its options had
    /// <c>background-color: rgba(0, 0, 0, 0)</c>, fully transparent, while <c>color</c> was
    /// <c>gray-100</c> from the page. WebView2's popup falls back to white for a transparent
    /// background, so the options were light grey on white — nearly invisible.
    /// </para>
    /// <para>
    /// The guard is on the <c>&lt;select&gt;</c> alone, and that is measured rather than chosen: the
    /// language select on the settings page has carried <c>bg-white dark:bg-gray-800</c> since it was
    /// written, styles <em>no</em> options, and nobody has ever reported its popup. Since
    /// <c>background-color</c> does not inherit, its options are transparent too — so the popup body
    /// comes from the select, and asserting on the options would demand a change the working example
    /// proves unnecessary. The alpha is the half that matters: a transparent background is the bug,
    /// whatever hue the theme picks.
    /// </para>
    /// <para>
    /// The colour is painted onto a 1×1 canvas and read back rather than parsed: Tailwind's palette
    /// is oklch, and a regex over the digits would read <c>oklch(0.967 0.003 264.542)</c> as a blue
    /// channel of 264. Same technique as <see cref="TodoApp.ContrastFailuresAsync"/>, for the same
    /// reason.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light, false)]
    [InlineData(ColorScheme.Dark, true)]
    public async Task Every_select_paints_its_own_background_so_its_popup_has_a_colour_to_inherit(
        ColorScheme scheme, bool expectDark)
    {
        await OpenAppAsync(new() { Width = 1400, Height = 900 }, scheme);
        var tasks = App.Tasks;

        // Through the UI rather than a builder, because the panel has to be open for the select to
        // exist at all - and the row is the only way to open it.
        await tasks.NewTaskInput.FillAsync("Vælgeren");
        await tasks.NewTaskInput.PressAsync("Enter");
        await tasks.RowTitled("Vælgeren").ClickAsync();
        await Assertions.Expect(tasks.StatusSelect).ToBeVisibleAsync();

        var measured = await App.Page.EvaluateAsync<string[]>(@"() => {
            const surface = document.createElement('canvas');
            surface.width = surface.height = 1;
            const ctx = surface.getContext('2d', { willReadFrequently: true });
            ctx.globalCompositeOperation = 'copy';

            const channels = (css) => {
                // Reset first: an unparseable colour leaves fillStyle alone, so without this it
                // would silently report the previous element's colour.
                ctx.fillStyle = '#000';
                ctx.fillStyle = css;
                ctx.fillRect(0, 0, 1, 1);
                const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
                return r + ',' + g + ',' + b + ',' + a;
            };

            // Every select on this screen rather than the status one by name, so a second select on
            // the task list inherits the rule instead of slipping past it. Today that is one: the
            // language select lives on the settings page and is not in reach here.
            return Array.from(document.querySelectorAll('select')).map((select, index) => {
                const name = select.getAttribute('data-testid') || 'select-' + index;
                return name + ' ' + channels(getComputedStyle(select).backgroundColor);
            });
        }");

        Assert.NotEmpty(measured);

        foreach (var entry in measured)
        {
            var parts = entry.Split(' ');
            var channels = parts[1].Split(',').Select(int.Parse).ToArray();
            var (red, green, blue, alpha) = (channels[0], channels[1], channels[2], channels[3]);

            Assert.True(alpha == 255,
                $"The {parts[0]} has no background of its own (alpha {alpha} of 255). WebView2 "
                + $"paints the dropdown from this and falls back to white when it is transparent, "
                + $"so the options end up on white however the theme is set - and color-scheme does "
                + $"not reach them, measured twice.");

            var isDark = (red + green + blue) / 3 < DarkBelow;

            Assert.True(isDark == expectDark,
                $"The {parts[0]}'s background does not follow the theme: rgb({red}, {green}, "
                + $"{blue}) reads as {(isDark ? "dark" : "light")} with prefers-color-scheme: "
                + $"{(expectDark ? "dark" : "light")}.");
        }
    }

    /// <summary>The mean channel, which is enough to tell near-black from white and does not pretend
    /// to be a perceptual measure - <see cref="ContrastTests"/> owns those.</summary>
    private static int Luminance(string rgb)
    {
        var channels = rgb
            .Replace("rgba(", string.Empty)
            .Replace("rgb(", string.Empty)
            .Replace(")", string.Empty)
            .Split(',')
            .Take(3)
            .Select(part => int.Parse(part.Trim()))
            .ToArray();

        Assert.Equal(3, channels.Length);

        return channels.Sum() / 3;
    }
}
