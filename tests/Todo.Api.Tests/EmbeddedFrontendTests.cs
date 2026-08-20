using System.Net;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The frontend is served out of the assembly, not off disk. Slice 16 embedded it so the published
/// exe is one file, and this is the guard on that.
/// <para>
/// It needs a content root with no wwwroot in it, and that is the whole design of the test rather
/// than a detail. Measured: with <c>UseStaticFiles()</c> put back to its default - reading the
/// content root - all 44 E2E journeys still passed, because every test host points its content root
/// at src\Todo.Host, where a real wwwroot sits on disk. So the entire suite was blind to the
/// embedding being undone, and the only thing that would have noticed was a published exe. A folder
/// the test creates and leaves empty is what makes the assembly the only place the bytes can come
/// from.
/// </para>
/// </summary>
public class EmbeddedFrontendTests
{
    /// <summary>
    /// index.html through the fallback, and the two hashed assets it asks for by name. The hashes
    /// change on every Angular build, so they are read out of index.html rather than written down -
    /// a pinned name would turn an ordinary rebuild into a red test.
    /// </summary>
    [Fact]
    public async Task The_frontend_is_served_from_the_assembly_when_the_content_root_has_no_wwwroot()
    {
        var contentRoot = Directory.CreateTempSubdirectory("TodoApp.Tests.EmptyRoot.");

        try
        {
            Assert.Empty(Directory.GetFileSystemEntries(contentRoot.FullName));

            await using var host = await RunningHost.StartAsync("--contentRoot", contentRoot.FullName);

            var index = await host.Client.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, index.StatusCode);

            var html = await index.Content.ReadAsStringAsync();

            // Not merely non-empty: a 200 with the wrong body is exactly what MapFallbackToFile
            // hands back when it finds something else, and CLAUDE.md has the measured version of
            // that trap - an unmapped GET under /api/ answers 200 with index.html in the body.
            Assert.Contains("<app-root", html);

            foreach (var asset in HashedAssets(html))
            {
                var response = await host.Client.GetAsync(asset);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.NotEqual(0, response.Content.Headers.ContentLength);
            }
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A translation file, which is the one served asset that lives in a subfolder. The manifest
    /// carries a directory tree rather than a flat list, and a provider rooted one level off would
    /// answer index.html and 404 this.
    /// </summary>
    [Fact]
    public async Task A_file_in_a_subfolder_of_the_embedded_frontend_is_served_too()
    {
        var contentRoot = Directory.CreateTempSubdirectory("TodoApp.Tests.EmptyRoot.");

        try
        {
            await using var host = await RunningHost.StartAsync("--contentRoot", contentRoot.FullName);

            var response = await host.Client.GetAsync("/i18n/da.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();

            // A key that has to be there, so this cannot pass on the fallback's index.html.
            Assert.Contains("\"settings\"", json);
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The hashed script and stylesheet index.html references, as root-relative paths. Reading them
    /// out of the served HTML keeps the test honest across rebuilds and, more importantly, means it
    /// asks for exactly what a browser would ask for.
    /// </summary>
    private static IEnumerable<string> HashedAssets(string html)
    {
        var assets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (prefix, suffix) in new[] { ("src=\"", ".js"), ("href=\"", ".css") })
        {
            var index = 0;

            while ((index = html.IndexOf(prefix, index, StringComparison.Ordinal)) >= 0)
            {
                index += prefix.Length;

                var end = html.IndexOf('"', index);
                var value = html[index..end];

                if (value.EndsWith(suffix, StringComparison.Ordinal) && !value.Contains("//"))
                {
                    assets.Add($"/{value.TrimStart('/')}");
                }
            }
        }

        // A set rather than a list, because the stylesheet is named twice - Angular emits a preload
        // link beside the stylesheet itself. Counted as a list this said three, and a test written
        // to expect two would have been wrong about the file rather than about the app.
        //
        // Both kinds asserted rather than a total, so the claim survives an Angular build that adds
        // a polyfill bundle. Without them the loop could find nothing and the test would pass on an
        // empty set - the same shape as a formatting check that matched no files.
        Assert.Contains(assets, a => a.EndsWith(".js", StringComparison.Ordinal));
        Assert.Contains(assets, a => a.EndsWith(".css", StringComparison.Ordinal));

        return assets;
    }
}
