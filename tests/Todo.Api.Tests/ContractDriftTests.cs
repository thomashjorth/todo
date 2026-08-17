using System.Text.Json;
using Todo.TestSupport;
using YamlDotNet.Serialization;

namespace Todo.Api.Tests;

/// <summary>
/// contracts/openapi.yaml owns the API surface. This fails the build when the
/// implementation drifts away from it in either direction.
/// </summary>
public class ContractDriftTests : ApiTest
{
    [Fact]
    public async Task Running_api_exposes_exactly_the_operations_in_the_contract()
    {
        var expected = OperationsFromContract();
        var actual = await OperationsFromRunningAppAsync();

        Assert.Equal(expected, actual);
    }

    private static SortedSet<string> OperationsFromContract()
    {
        var yaml = File.ReadAllText(RepoPaths.ContractFile);
        var document = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(yaml);

        var paths = (Dictionary<object, object>)document["paths"];
        var operations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, verbs) in paths)
        {
            foreach (var verb in ((Dictionary<object, object>)verbs).Keys)
            {
                operations.Add($"{verb.ToString()!.ToUpperInvariant()} {path}");
            }
        }

        return operations;
    }

    private async Task<SortedSet<string>> OperationsFromRunningAppAsync()
    {
        using var stream = await Client.GetStreamAsync("/openapi/v1.json");
        using var document = await JsonDocument.ParseAsync(stream);

        var operations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var verb in path.Value.EnumerateObject())
            {
                operations.Add($"{verb.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        return operations;
    }
}
