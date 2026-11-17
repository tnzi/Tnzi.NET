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
    /// The external agent waits for the person attending the turn: to be allowed an action (<c>kind</c> <c>permission</c>,
    /// the "ask me" and "plan" permission modes), for answers to its questions (<c>question</c>), or for its plan to be approved (<c>plan</c>).
    /// <c>eventData</c>: <c>requestId</c>, <c>kind</c>, <c>tool</c>, <c>toolCallId</c>, <c>input</c>, <c>description</c>, <c>title</c>, <c>reason</c>,
    /// <c>blockedPath</c>, <c>questions</c> (<c>question</c> / <c>header</c> / <c>options</c> of <c>label</c> and <c>description</c> / <c>multiSelect</c>),
    /// <c>plan</c>, <c>options</c> (<c>id</c> / <c>name</c> / <c>kind</c>), <c>expiresAt</c>, <c>status</c> (<c>Pending</c>).
    /// Answer with <c>POST chat/runs/{runId}/permissions/{requestId}</c> (<c>answers</c> for a question).
    /// </summary>
    public const string PermissionRequest = "permission_request";

    /// <summary>
    /// A request got its outcome. <c>eventData</c>: <c>requestId</c>, <c>kind</c>, <c>status</c>
    /// (<c>Allowed</c> / <c>Denied</c> / <c>TimedOut</c> / <c>Cancelled</c>), <c>optionId</c>, <c>answeredBy</c>,
    /// and for an answered question <c>answers</c> (question text to the answers given).
    /// </summary>
    public const string PermissionResolved = "permission_resolved";

    /// <summary>
    /// The agent finished one answer but the run goes on: the CLI still has background tasks running and starts another
    /// turn by itself when they finish. <c>eventData</c>: <c>assistantMessageId</c> (the answer, already stored in the
    /// thread) and <c>backgroundTasks</c> (<c>id</c> / <c>description</c> / <c>kind</c>). Close the current answer here;
    /// text after this event belongs to a new answer in the same thread. The terminal chunk carries the last one.
    /// The conversation is not busy after it: the next message of the thread takes over the run's process (its background
    /// tasks keep running) and this run ends at the answer it has given, so a client may close this stream when it sends
    /// one (without cancelling the run).
    /// </summary>
    public const string TurnCompleted = "turn_completed";

    /// <summary>
    /// The CLI compacted the conversation's context (<c>/compact</c>, or automatically when the context was nearly
    /// full). <c>eventData</c>: <c>trigger</c> (<c>manual</c> / <c>auto</c>), <c>preTokens</c>, <c>postTokens</c>.
    /// A turn that only compacted has no answer text; its stored reply has status <c>compacted</c>.
    /// </summary>
    public const string Compacted = "compacted";

    /// <summary>
    /// A message the user queued while the turn ran reached the agent. <c>eventData</c>: <c>messageId</c> (the queued
    /// message), <c>taken</c>, <c>text</c>; when taken also <c>threadMessageId</c> (the user message it became in the
    /// thread) and <c>assistantMessageId</c> (the answer up to that point, stored as its own message; absent when the
    /// agent had said nothing yet); when not taken <c>reason</c> (the message is held in the queue). Close the current
    /// answer at a taken one, show the user's message, and let the text after it start a new answer: the conversation then
    /// reads the same as after a reload.
    /// </summary>
    public const string UserMessage = "user_message";
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

    /// <summary>
    /// The turn compacted the conversation's context (<c>/compact</c>) and had no answer of its own; the reply has no text.
    /// Written so the conversation shows "conversation compacted" after a reload.
    /// </summary>
    public const string Compacted = "compacted";
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
