using System.Collections.Concurrent;

namespace Todo.Host.Ai;

/// <summary>
/// The tasks' vectors, kept in memory and made again only when a task's text changes. In memory
/// rather than in SQLite, because embedding measured 1.4 s per hundred tasks on the user's CPU:
/// the first search after a start pays that once, and a table would cost a migration to save it.
/// See section 6 of the remaining-features design.
/// <para>
/// The text itself is the cache key rather than a hash of it: a hash is only worth having when the
/// text is too big to hold, and a task's embedded text is cut to a title and 600 characters.
/// Deleted tasks are never pruned - every caller asks by the ids it has, so a stale entry is only
/// memory, a few kilobytes a task.
/// </para>
/// </summary>
public sealed class TaskEmbeddings
{
    /// <summary>One request's worth: Ollama embeds a batch in one pass, and a hundred took 1.4 s.</summary>
    private const int BatchSize = 64;

    private readonly ConcurrentDictionary<long, (string Text, float[] Vector)> _cache = new();

    /// <summary>
    /// Two searches at once would both find the same tasks missing and embed them twice. The gate
    /// makes the second one wait for the first and then find them in the cache.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<(long Id, float[] Vector)>> ForAsync(
        OllamaClient ollama, IReadOnlyList<(long Id, string Text)> tasks, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            var missing = tasks
                .Where(t => !_cache.TryGetValue(t.Id, out var cached) || cached.Text != t.Text)
                .ToList();

            foreach (var batch in missing.Chunk(BatchSize))
            {
                var vectors = await ollama.EmbedAsync([.. batch.Select(t => t.Text)], ct);

                for (var i = 0; i < batch.Length; i++)
                {
                    _cache[batch[i].Id] = (batch[i].Text, vectors[i]);
                }
            }

            return [.. tasks.Select(t => (t.Id, _cache[t.Id].Vector))];
        }
        finally
        {
            _gate.Release();
        }
    }
}
