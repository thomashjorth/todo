using Todo.Core.Settings;

namespace Todo.TestSupport.Builders;

/// <summary>A factory rather than a builder: an alias is one string, so there is nothing to chain.</summary>
public static class UserAliases
{
    public static UserAlias Named(string value) => new() { Value = value };
}
