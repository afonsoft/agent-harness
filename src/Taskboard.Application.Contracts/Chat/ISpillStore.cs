namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-006: durable store for spilled
/// tool outputs under <c>~/.agent-harness/spill/{runId}/</c> — the wire keeps a
/// capped head + <c>spill://{id}</c> pointer the model can page back via
/// <c>read_file</c>.
/// </summary>
public interface ISpillStore
{
    /// <summary>
    /// Persists content as <c>spill/{runId}/{runId}-{seq}</c>. Ids are
    /// deterministic per run (<c>{runId}-{seq}</c>); returns the spill id.
    /// </summary>
    string Save(string runId, int seq, string content);

    /// <summary>
    /// Reads a character slice of a spill file. Null when the id is malformed
    /// or the file is gone (deleted run, retention sweep).
    /// </summary>
    (string Content, int TotalChars)? Read(string spillId, int offset = 0, int limit = 16000);

    /// <summary>Deletes the whole <c>spill/{runId}/</c> directory (retention).</summary>
    void DeleteRunDir(string runId);

    /// <summary>
    /// RF-008 boot sweep: removes spill dirs whose run id is not in
    /// <paramref name="aliveRunIds"/>. Returns the number removed.
    /// </summary>
    int SweepOrphans(IReadOnlySet<string> aliveRunIds);
}
