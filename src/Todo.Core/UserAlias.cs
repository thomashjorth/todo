namespace Todo.Core;

public class UserAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Value { get; set; } = string.Empty;
}
