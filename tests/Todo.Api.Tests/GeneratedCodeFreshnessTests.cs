using System.Security.Cryptography;
using System.Text;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class GeneratedCodeFreshnessTests
{
    [Fact]
    public void Generated_code_matches_the_current_contract()
    {
        var hashFile = Path.Combine(
            RepoPaths.Root, "src", "Todo.Contracts", "Generated", ".source-hash");

        Assert.True(File.Exists(hashFile),
            "Generated code is missing. Run scripts/generate-api.ps1.");

        var recorded = File.ReadAllText(hashFile).Trim();

        // Must match the normalisation in scripts/generate-api.ps1, or the hash
        // becomes dependent on the checkout's line endings.
        var normalized = File.ReadAllText(RepoPaths.ContractFile).Replace("\r\n", "\n");
        var current = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

        Assert.True(
            string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase),
            "contracts/openapi.yaml changed without regenerating. Run scripts/generate-api.ps1 and commit the result.");
    }
}
