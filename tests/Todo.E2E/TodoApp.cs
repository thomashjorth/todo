using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

/// <summary>
/// Owns the page and the routes. Navigation waits for the destination before it hands a test the
/// screen, so no test can look for an element that has not rendered yet.
/// </summary>
public sealed class TodoApp
{
    private TodoApp(IPage page) => Page = page;

    public IPage Page { get; }

    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Name = "Mandalorian ToDo", Exact = true });

    public ILocator Health => Page.GetByTestId("health");

    public TaskListScreen Tasks => new(this);

    public static async Task<TodoApp> OpenAsync(
        IBrowser browser, RunningHost host, ViewportSize? viewport = null,
        ColorScheme? colorScheme = null)
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = viewport,
            ColorScheme = colorScheme,
        });
        await page.GotoAsync(host.BaseUrl);

        var app = new TodoApp(page);
        await app.Tasks.WaitUntilShownAsync();

        return app;
    }

    public async Task<TaskListScreen> GoToTasks()
    {
        await Page.GetByTestId("nav-tasks").ClickAsync();

        var screen = new TaskListScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<RetroImportScreen> GoToImport()
    {
        await Page.GetByTestId("nav-import").ClickAsync();

        var screen = new RetroImportScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<SettingsScreen> GoToSettings()
    {
        await Page.GetByTestId("nav-settings").ClickAsync();

        var screen = new SettingsScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    /// <summary>
    /// Reloads and hands back the task list, so a test can tell what the app saved from what it
    /// only holds in memory.
    /// </summary>
    public async Task<TaskListScreen> ReloadAsync()
    {
        await Page.ReloadAsync();

        var screen = new TaskListScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public Task<int> ScrollWidthAsync()
        => Page.EvaluateAsync<int>("document.documentElement.scrollWidth");

    /// <summary>
    /// The width the page has to lay out in, which a vertical scrollbar makes narrower than
    /// the viewport.
    /// </summary>
    public Task<int> ClientWidthAsync()
        => Page.EvaluateAsync<int>("document.documentElement.clientWidth");

    /// <summary>
    /// Every element that renders its own text, measured against the background that actually
    /// sits behind it. The browser has already resolved which background that is, which is why
    /// this runs in the page rather than over the class attributes.
    /// </summary>
    public Task<string[]> ContrastFailuresAsync() => Page.EvaluateAsync<string[]>(
        """
        () => {
          // Tailwind's palette is oklch, and getComputedStyle hands back the colour in the space
          // it was authored in — so a regex over the digits would read oklch(0.967 0.003 264.542)
          // as a blue channel of 264. Painting the colour and reading the pixel back makes the
          // browser do the conversion, for every syntax it supports.
          const surface = document.createElement('canvas');
          surface.width = surface.height = 1;
          const ctx = surface.getContext('2d', { willReadFrequently: true });
          ctx.globalCompositeOperation = 'copy';

          const channels = (css) => {
            // Reset first: an unparseable colour leaves fillStyle alone, and without this it
            // would silently inherit whatever the previous element was.
            ctx.fillStyle = '#000';
            ctx.fillStyle = css;
            ctx.fillRect(0, 0, 1, 1);

            const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
            return [r, g, b, a / 255];
          };

          const over = ([r, g, b, a], [br, bg, bb]) =>
            [r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)];

          const luminance = ([r, g, b]) => {
            const lin = [r, g, b].map((v) => {
              v /= 255;
              return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
            });
            return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2];
          };

          const ratio = (fg, bg) => {
            const [a, b] = [luminance(fg), luminance(bg)];
            const [hi, lo] = a > b ? [a, b] : [b, a];
            return (hi + 0.05) / (lo + 0.05);
          };

          // Walk up until something is actually painted: a transparent background means the
          // ancestor's colour is what the user sees behind the text. Translucent layers are kept
          // and composited, so a wash over a dark panel is not mistaken for the wash alone.
          const backgroundOf = (el) => {
            const layers = [];

            for (let n = el; n; n = n.parentElement) {
              const layer = channels(getComputedStyle(n).backgroundColor);
              if (layer[3] > 0) {
                layers.push(layer);
                if (layer[3] >= 1) break;
              }
            }

            // Nothing opaque underneath: what shows through is the browser's own canvas, and
            // this app declares no color-scheme, so that canvas is white in both themes.
            return layers.reduceRight((under, layer) => over(layer, under), [255, 255, 255]);
          };

          const hidden = (s) =>
            s.display === 'none' || s.visibility === 'hidden' || Number(s.opacity) === 0;

          const label = (el) =>
            el.tagName.toLowerCase() + (el.dataset.testid ? `[${el.dataset.testid}]` : '');

          const failures = [];

          const check = (el, style, fg, what, sample) => {
            // WCAG large text: 24px, or 18.66px when bold.
            const size = parseFloat(style.fontSize);
            const large = size >= 24 || (Number(style.fontWeight) >= 700 && size >= 18.66);
            const needed = large ? 3 : 4.5;
            const bg = backgroundOf(el);

            // Text can be translucent too, and then the background shows through the glyphs.
            const r = ratio(over(fg, bg), bg);

            if (r < needed) {
              failures.push(
                `${label(el)} ${what} "${sample.slice(0, 40)}" ${r.toFixed(2)}:1 needs ${needed}`);
            }
          };

          for (const el of document.querySelectorAll('body *')) {
            const style = getComputedStyle(el);
            if (hidden(style)) continue;

            // Only the element's own text: a parent would otherwise be blamed for its child's.
            const own = [...el.childNodes]
              .filter((n) => n.nodeType === Node.TEXT_NODE)
              .map((n) => n.textContent.trim())
              .join(' ')
              .trim();

            if (own) check(el, style, channels(style.color), 'text', own);

            // Placeholder colour lives on ::placeholder, so the walk above cannot see it —
            // and it is text the user reads every time the app opens.
            if (el instanceof HTMLInputElement && el.placeholder) {
              const ph = getComputedStyle(el, '::placeholder');
              check(el, ph, channels(ph.color), 'placeholder', el.placeholder);
            }
          }

          return failures;
        }
        """);
}
