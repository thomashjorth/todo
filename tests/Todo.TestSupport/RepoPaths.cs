namespace Todo.TestSupport;

public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string HostContentRoot => Path.Combine(Root, "src", "Todo.Host");

    public static string ContractFile => Path.Combine(Root, "contracts", "openapi.yaml");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Todo.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Todo.sln above the test output directory.");
    }
}
