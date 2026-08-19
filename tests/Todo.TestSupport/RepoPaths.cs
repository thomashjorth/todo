namespace Todo.TestSupport;

public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string HostContentRoot => Path.Combine(Root, "src", "Todo.Host");

    public static string ContractFile => Path.Combine(Root, "contracts", "openapi.yaml");

    public static string WebRoot => Path.Combine(Root, "src", "Todo.Web");

    public static string WebTsConfigFile(string fileName) =>
        Path.Combine(WebRoot, fileName);

    /// <summary>
    /// A translation file, by name: "da.json" or "en.json". The source files rather than a built
    /// copy in wwwroot, because a guard on the built copy would go green on a stale build.
    /// </summary>
    public static string WebI18nFile(string fileName) =>
        Path.Combine(WebRoot, "public", "i18n", fileName);

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
