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

    /// <summary>
    /// The browser's language, pinned for every page the suite opens. A database with no stored
    /// language follows the system's, and Playwright's browser otherwise takes the machine's: Danish on
    /// the developer's machine, en-US on GitHub's runner. Unpinned, the whole suite - which reads
    /// Danish text - was green on one and red on the other, and CI failed from September 2026 until
    /// this line, on every run, with nobody outside a login able to read why. Measured from the first
    /// run that published its failures as annotations.
    /// </summary>
    private const string Locale = "da-DK";

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
            Locale = Locale,
        });

        var app = new TodoApp(page);
        await ReplaceWindowOpenAsync(page, app.RecordOpenedLink);

        // Through the API rather than the page: page.APIRequest shares its cookies with the page, so
        // one call logs the browser in, and the page itself is measured once, in LoginJourneyTests.
        // Every journey still runs the login route and the cookie on every request it makes.
        var login = await page.APIRequest.PostAsync($"{host.BaseUrl}/api/auth/login", new()
        {
            DataObject = new { password = RunningHost.TestPassword },
        });
        Assert.Equal(204, login.Status);

        await page.GotoAsync(host.BaseUrl);
        await app.Tasks.WaitUntilShownAsync();

        return app;
    }

    /// <summary>The app as somebody without the cookie meets it: the login page.</summary>
    public static async Task<TodoApp> OpenLoggedOutAsync(
        IBrowser browser, RunningHost host, ViewportSize? viewport = null,
        ColorScheme? colorScheme = null)
    {
        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = viewport,
            ColorScheme = colorScheme,
            Locale = Locale,
        });

        var app = new TodoApp(page);
        await ReplaceWindowOpenAsync(page, app.RecordOpenedLink);

        await page.GotoAsync(host.BaseUrl);
        await Assertions.Expect(app.Login.Password).ToBeVisibleAsync();

        return app;
    }

    public LoginScreen Login => new(this);

    private readonly List<string> _openedLinks = [];

    private void RecordOpenedLink(string url)
    {
        lock (_openedLinks)
        {
            _openedLinks.Add(url);
        }
    }

    /// <summary>
    /// Every link the app opens goes through window.open, and a test browser has no business following
    /// one out of the process: the target is an external system or a fake server that has already
    /// stopped. Replaced on every page rather than per test, so a new button cannot forget it - the gap
    /// the per-test abort of /api/system/open-link left open until 2.0.0.
    /// </summary>
    /// <remarks>
    /// The record goes through an exposed function rather than an array on window, because an exposed
    /// function outlives a reload and an array would be emptied by one: a test that reloads before it
    /// clicks would otherwise count from a list that forgot what came before.
    /// </remarks>
    public static async Task ReplaceWindowOpenAsync(IPage page, Action<string>? record = null)
    {
        await page.ExposeFunctionAsync("__recordOpenedLink", (string url) => record?.Invoke(url));
        await page.AddInitScriptAsync("""
            window.open = (url) => {
              window.__recordOpenedLink(String(url));
              return null;
            };
            """);
    }

    /// <summary>Waits for the app to open its n-th link, and answers with it.</summary>
    public async Task<string> OpenedLinkAsync(int ordinal = 1)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            lock (_openedLinks)
            {
                if (_openedLinks.Count >= ordinal)
                {
                    return _openedLinks[ordinal - 1];
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The app opened {_openedLinks.Count} link(s) within ten seconds, not {ordinal}.");
            }

            await Task.Delay(50);
        }
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

    public async Task<JiraImportScreen> GoToJira()
    {
        await Page.GetByTestId("nav-jira").ClickAsync();

        var screen = new JiraImportScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<AdoImportScreen> GoToAdo()
    {
        await Page.GetByTestId("nav-ado").ClickAsync();

        var screen = new AdoImportScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<ReviewScreen> GoToReview()
    {
        await Page.GetByTestId("nav-review").ClickAsync();

        var screen = new ReviewScreen(this);
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
    /// What the browser paints on the focused element, as `testid|outlineStyle|outlineWidth|outlineColor`.
    /// The focused element is part of the answer on purpose: a helper that returns a friendly
    /// sentinel when nothing is focused reports the one failure it exists to catch as success.
    /// </summary>
    public Task<string> FocusOutlineAsync() => Page.EvaluateAsync<string>(
        """
        () => {
          const el = document.activeElement;
          if (!el || el === document.body) return 'none|no-outline|0px|transparent';

          const s = getComputedStyle(el);
          const id = el.dataset.testid ?? el.tagName.toLowerCase();

          return `${id}|${s.outlineStyle}|${s.outlineWidth}|${s.outlineColor}`;
        }
        """);

    /// <summary>
    /// Every element that renders its own text, measured against the background that actually
    /// sits behind it. The browser has already resolved which background that is, which is why
    /// this runs in the page rather than over the class attributes.
    /// </summary>
    public Task<string[]> ContrastFailuresAsync() => ContrastFailuresAsync(Page.MainFrame);

    /// <summary>
    /// The same walk inside one frame. The helper's Connections page is on another origin, so the
    /// page's own walk cannot reach into it; Playwright runs this in the frame, and the frame's own
    /// opaque body stops the climb for a background there.
    /// </summary>
    public static Task<string[]> ContrastFailuresAsync(IFrame frame) => frame.EvaluateAsync<string[]>(ContrastScript);

    private const string ContrastScript =
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

            // Nothing opaque underneath: what shows through is the browser's own canvas. The app
            // declares color-scheme: light dark, so that canvas follows the theme — but <body> is
            // opaque in both, so this fallback never fires. White is only the last resort.
            return layers.reduceRight((under, layer) => over(layer, under), [255, 255, 255]);
          };

          const hidden = (s) =>
            s.display === 'none' || s.visibility === 'hidden' || Number(s.opacity) === 0;

          const label = (el) =>
            el.tagName.toLowerCase() + (el.dataset.testid ? `[${el.dataset.testid}]` : '');

          const paintsItsValue = new Set([
            'text', 'search', 'email', 'url', 'tel', 'password', 'number',
            'date', 'datetime-local', 'month', 'week', 'time',
          ]);

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
              // One line per failure, so a multi-line note in a field cannot break the list the
              // caller reads as its work list.
              const quoted = sample.replace(/\s+/g, ' ').slice(0, 40);

              failures.push(`${label(el)} ${what} "${quoted}" ${r.toFixed(2)}:1 needs ${needed}`);
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

            // A field's value is a property rather than a child text node, so the walk above is
            // blind to it: a note editor painted at 2.6:1 passed this test until the value was
            // measured here. Only the types that paint their value as text are asked — a
            // checkbox's value is the string "on", and blaming a box for it would be a failure
            // nobody can see or fix.
            const painted = el instanceof HTMLTextAreaElement
              || (el instanceof HTMLInputElement && paintsItsValue.has(el.type));

            if (painted && el.value.trim()) {
              check(el, style, channels(style.color), 'value', el.value.trim());
            }

            // Placeholder colour lives on ::placeholder, so the walk above cannot see it —
            // and it is text the user reads every time the app opens. A <textarea> paints one
            // too: the condition named only HTMLInputElement until this line, so a
            // `<textarea placeholder="…">` was never measured. None exists today, which means
            // this half of the condition is unproven rather than proven harmless.
            if ((el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement)
              && el.placeholder) {
              const ph = getComputedStyle(el, '::placeholder');
              check(el, ph, channels(ph.color), 'placeholder', el.placeholder);
            }
          }

          return failures;
        }
        """;
}
