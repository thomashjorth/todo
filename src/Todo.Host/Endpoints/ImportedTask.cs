using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Host.Endpoints;

/// <summary>
/// What the app already holds under one external key, as the two import previews need to read it.
///
/// The status is what turns a one-off suggestion into one that stops coming back: a closure is only
/// offered while the local task is <em>not</em> done, and a bare set of keys cannot say that. The
/// title is what a reconciled row falls back on when the source answers nothing at all - no answer
/// came back to take one from, so the local task's title is the only one there is.
///
/// Shared by both endpoints rather than declared twice. They are written as twins, so a second copy
/// would be a second thing to keep in step with nothing able to see the two drift apart.
/// </summary>
internal sealed record ImportedTask(CoreStatus Status, string Title);
