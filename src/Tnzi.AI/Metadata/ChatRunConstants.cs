namespace Tnzi.AI.Metadata;

/// <summary>
/// Event types a chat stream carries about the run behind it (in <c>StreamEvent.EventType</c>).
/// </summary>
public static class RunStreamEventTypes
{
    /// <summary>
    /// First event of a turn that runs on an external CLI runtime, and of a reattached stream: carries the run id
    /// (to reattach or cancel it), the thread id and the id of the stored user message.
    /// </summary>
    public const string RunStarted = "run_started";

    /// <summary>
    /// The external agent asks to be allowed an action (the "ask me" permission mode, with someone attending the turn).
    /// <c>eventData</c>: <c>requestId</c>, <c>tool</c>, <c>toolCallId</c>, <c>input</c>, <c>description</c>, <c>title</c>, <c>reason</c>,
    /// <c>blockedPath</c>, <c>options</c> (<c>id</c> / <c>name</c> / <c>kind</c>), <c>expiresAt</c>, <c>status</c> (<c>Pending</c>).
    /// Answer with <c>POST chat/runs/{runId}/permissions/{requestId}</c>.
    /// </summary>
    public const string PermissionRequest = "permission_request";

    /// <summary>
    /// A permission request got its outcome. <c>eventData</c>: <c>requestId</c>, <c>status</c>
    /// (<c>Allowed</c> / <c>Denied</c> / <c>TimedOut</c> / <c>Cancelled</c>), <c>optionId</c>, <c>answeredBy</c>.
    /// </summary>
    public const string PermissionResolved = "permission_resolved";
}

/// <summary>
/// Statuses of one tool call (<see cref="Engine.ToolCallDetail.Status"/>).
/// </summary>
public static class ToolCallStatuses
{
    /// <summary>The agent made the call; no result yet.</summary>
    public const string Running = "running";

    /// <summary>The tool returned a result.</summary>
    public const string Completed = "completed";

    /// <summary>The tool reported an error.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// How a stored assistant reply ended, when it did not simply complete (<c>AgentThreadMessage.Status</c>).
/// </summary>
/// <remarks>
/// A reply that completed has no status. The built-in engine only stores replies that completed, so only replies of
/// external runs carry one: a run the user stopped keeps what it had written and the calls it had made, and so does a
/// run that failed or timed out after producing some of its answer.
/// </remarks>
public static class ThreadMessageStatuses
{
    /// <summary>The user stopped the run; the reply is what it had written by then.</summary>
    public const string Stopped = "stopped";

    /// <summary>The run failed or timed out; the reply is what it had written by then.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// The CLI's account reached its usage limit; the reply is what it had written by then (often nothing), and
    /// <c>UsageLimitResetsAt</c> says when the limit resets when the CLI reported it. Written even without content, so the
    /// conversation shows the outcome after a reload.
    /// </summary>
    public const string UsageLimit = "usage_limit";
}

/// <summary>
/// Where a stored attachment came from.
/// </summary>
public enum AttachmentOrigin
{
    /// <summary>The user attached it to a chat message.</summary>
    Upload = 0,

    /// <summary>A tool returned it (an image block in a tool result).</summary>
    ToolResult = 1
}
