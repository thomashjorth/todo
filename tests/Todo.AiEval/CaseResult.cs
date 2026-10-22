namespace Todo.AiEval;

public sealed record CaseResult(string Feature, string Name, double Seconds, List<string> Failures);
