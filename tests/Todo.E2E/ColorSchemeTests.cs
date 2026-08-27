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
/// This is that blind spot's guard, and it exists because the blind spot cost a real bug: the class
/// sat on <c>&lt;body&gt;</c>, where it styles the body and everything under it but leaves the root
/// at <c>color-scheme: normal</c>. Measured 2026-08-27 with the preference on dark: the root's
/// <c>Canvas</c> was <c>rgb(255, 255, 255)</c> while the body's was <c>rgb(18, 18, 18)</c>, so the
/// status dropdown opened white under a dark app, with its options' text still inheriting the dark
/// theme's light grey — nearly invisible.
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
