using System.Net;
using System.Text.Json;
using Todo.TestSupport;
using YamlDotNet.Serialization;

namespace Todo.Api.Tests;

/// <summary>
/// The app serves two OpenAPI documents on purpose, with different jobs.
/// <c>/openapi/v1.json</c> is derived from the code, so it is the truth about what is actually
/// implemented - <see cref="ContractDriftTests"/> reads that one and must keep being able to.
/// <c>/openapi/contract.yaml</c> is contracts/openapi.yaml verbatim, and it is what the
/// documentation page reads, because the derivation carries none of the prose a person opens a
/// documentation page to read.
///
/// The two are easy to mix up - they have the same 15 operations and the same 22 schemas - so
/// these tests hold the distinction in place.
/// </summary>
public class ContractDocumentTests : ApiTest
{
    private const string ContractRoute = "/openapi/contract.yaml";
    private const string RuntimeRoute = "/openapi/v1.json";

    [Fact]
    public async Task Contract_route_serves_contracts_openapi_yaml_verbatim()
    {
        var response = await Client.GetAsync(ContractRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/yaml", response.Content.Headers.ContentType?.MediaType);

        // The response comes from an embedded resource, which is a copy and can therefore go
        // stale: edit contracts/openapi.yaml without rebuilding the host and the page would keep
        // showing yesterday's document. Comparing against the file on disk is what says so.
        //
        // Line endings are normalised because the working copy is CRLF and nothing promises the
        // embedded bytes are - a difference there would be noise, not drift.
        var served = Normalise(await response.Content.ReadAsStringAsync());
        var onDisk = Normalise(await File.ReadAllTextAsync(RepoPaths.ContractFile));

        Assert.Equal(onDisk, served);
    }

    /// <summary>
    /// Guards the reason the route exists. Serving the derivation instead would look like a
    /// simplification - same paths, same schemas, one document fewer - and this is what stands in
    /// the way. The assertions are two-sided on purpose: each one names something the contract has
    /// and the derivation lacks, so neither can pass on a property both documents share.
    /// </summary>
    [Fact]
    public async Task Contract_route_serves_the_contract_and_not_the_derived_document()
    {
        var served = ParseYaml(await Client.GetStringAsync(ContractRoute));
        using var runtime = JsonDocument.Parse(await Client.GetStringAsync(RuntimeRoute));
        var onDisk = ParseYaml(await File.ReadAllTextAsync(RepoPaths.ContractFile));

        var servedTitle = TitleOf(served);
        var contractTitle = TitleOf(onDisk);
        var runtimeTitle = runtime.RootElement.GetProperty("info").GetProperty("title").GetString();

        // "Todo API" against a derived title as this is written. Neither side is spelled out here.
        // The contract title comes from the file, so renaming the API does not fail a test about
        // which document is served - and the derived title is only ever compared, never named,
        // because it is built from the entry assembly: the app serves "Todo.Host | v1", but under
        // the test runner the very same code serves "Todo.Api.Tests | v1". Spelling it out would
        // have made this test fail for a reason that has nothing to do with the claim.
        Assert.Equal(contractTitle, servedTitle);
        Assert.NotEqual(contractTitle, runtimeTitle);

        // Prose is the whole point. Four of the fifteen operations carry a summary in the
        // contract; the derivation carries none. Asserting the served count against the file
        // keeps this honest if more summaries are written, while the zero is what makes the
        // comparison mean anything - if the derivation ever grows summaries of its own, the
        // premise behind this route has changed and this is the line that should say so.
        Assert.Equal(SummaryCountOf(onDisk), SummaryCountOf(served));
        Assert.NotEqual(0, SummaryCountOf(served));
        Assert.Equal(0, RuntimeSummaryCount(runtime));
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n");

    private static Dictionary<string, object> ParseYaml(string yaml) =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(yaml);

    private static string TitleOf(Dictionary<string, object> document) =>
        (string)((Dictionary<object, object>)document["info"])["title"];

    private static int SummaryCountOf(Dictionary<string, object> document)
    {
        var paths = (Dictionary<object, object>)document["paths"];

        return (from verbs in paths.Values
                from operation in ((Dictionary<object, object>)verbs).Values
                where ((Dictionary<object, object>)operation).ContainsKey("summary")
                select operation).Count();
    }

    private static int RuntimeSummaryCount(JsonDocument document) =>
        (from path in document.RootElement.GetProperty("paths").EnumerateObject()
         from operation in path.Value.EnumerateObject()
         where operation.Value.TryGetProperty("summary", out _)
         select operation).Count();
}
