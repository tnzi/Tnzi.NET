namespace Tnzi.AI.Engine;

/// <summary>
/// Tool call execution detail (name + duration, for client-side benchmarking)
/// </summary>
/// <remarks>
/// <para>
/// The built-in engine reports a call once it has run: name, duration and outcome.
/// </para>
/// <para>
/// A run on an external CLI runtime reports each call twice under the same <see cref="Id"/>: when the agent makes it
/// (<see cref="Status"/> <c>running</c>, with <see cref="Input"/>) and when its result arrives (<c>completed</c> or
/// <c>failed</c>, with <see cref="Result"/>). Clients merge the updates by id. The reply stored on the thread carries
/// one merged entry per call, so a reloaded conversation shows the same cards the stream showed.
/// </para>
/// <para>
/// The fields added for that are left out of the JSON when they are null, so the entries the built-in engine stores
/// keep their shape.
/// </para>
/// </remarks>
public class ToolCallDetail
{
    /// <summary>Tool name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Execution duration in milliseconds</summary>
    public double DurationMs { get; set; }

    /// <summary>Whether the tool call succeeded</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>Error message (only when IsSuccess is false)</summary>
    public string? Error { get; set; }

    /// <summary>Tool call id: pairs the updates of one call.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>The arguments the agent called the tool with, as the JSON object it sent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    /// <summary>Call status: <c>running</c>, <c>completed</c> or <c>failed</c> (see <see cref="ToolCallStatuses"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    /// <summary>
    /// What the tool returned: text and image blocks, in order. Text is capped per call and images are stored
    /// as attachments and referenced by id (limits in the AI.Cli module documentation).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolResultBlockDto>? Result { get; set; }

    /// <summary>
    /// How many characters of the reply text had been written when the call was made. Lets a client place the
    /// call card between the paragraphs it came between, live and after a reload alike.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TextOffset { get; set; }

    /// <summary>
    /// The <see cref="Id"/> of the sub-agent dispatch this call was made in (a call the sub-agent made, not the main
    /// agent). Null for the main agent's own calls. A client lists such calls under the dispatch's card, not in the reply.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    /// <summary>
    /// Set when this call started a CLI task: a sub-agent (claude's <c>Agent</c> tool) or a command running in the
    /// background. What it is, how far it got and, for a sub-agent, what it did. The call itself completes when it
    /// returns, which for a background task is at once; <see cref="ToolTaskDetail.Status"/> is the task's own state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolTaskDetail? Task { get; set; }

    /// <summary>A copy that shares nothing mutable with this instance.</summary>
    public ToolCallDetail Copy()
    {
        var copy = (ToolCallDetail)MemberwiseClone();
        copy.Result = Result?.ToList();
        copy.Task = Task?.Copy();
        return copy;
    }
}

/// <summary>
/// A task an external CLI agent started (<see cref="ToolCallDetail.Task"/>): a sub-agent, or a command running in the background.
/// </summary>
/// <remarks>
/// A sub-agent's tool calls are entries of the same reply's tool-call list with <see cref="ToolCallDetail.ParentId"/> set to the
/// dispatch's id; <see cref="Steps"/> gives the order of those calls and of the text the sub-agent wrote. A sub-agent
/// the sub-agent dispatched in turn is one of those calls, with a <see cref="ToolCallDetail.Task"/> of its own.
/// </remarks>
public class ToolTaskDetail
{
    /// <summary>The CLI's task id: what stopping the task takes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskId { get; set; }

    /// <summary><c>agent</c> (a sub-agent), <c>command</c> (a command in the background), or the CLI's own word for another kind.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>The model a sub-agent answers with, when its replies were seen (a nested sub-agent's are not streamed).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>The sub-agent's type (claude's <c>subagent_type</c>, e.g. <c>general-purpose</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentType { get; set; }

    /// <summary>The short description it was dispatched with.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>The task it was given.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Prompt { get; set; }

    /// <summary>Whether it runs in the background (the dispatch returned at once; the CLI is told when it finishes).</summary>
    public bool Background { get; set; }

    /// <summary><c>running</c>, <c>completed</c>, <c>failed</c> or <c>stopped</c>.</summary>
    public string Status { get; set; } = "running";

    /// <summary>When it started (UTC), for a client to show how long it has been running.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartedAt { get; set; }

    /// <summary>How long it ran, in milliseconds, as the CLI last reported.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    /// <summary>How many tool calls it made, as the CLI last reported (it also counts calls of sub-agents it dispatched, whose own calls are not streamed).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ToolUseCount { get; set; }

    /// <summary>Tokens it used, as the CLI last reported.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalTokens { get; set; }

    /// <summary>What it is doing now (e.g. "Reading alpha.txt").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Activity { get; set; }

    /// <summary>The report it handed back when it finished.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    /// <summary>What it did, in order: the text it wrote and its tool calls (by id).</summary>
    public List<ToolTaskStep> Steps { get; set; } = [];

    /// <summary>A copy that shares nothing mutable with this instance.</summary>
    public ToolTaskDetail Copy()
    {
        var copy = (ToolTaskDetail)MemberwiseClone();
        copy.Steps = Steps.Select(s => new ToolTaskStep { Type = s.Type, Text = s.Text, ToolCallId = s.ToolCallId }).ToList();
        return copy;
    }
}

/// <summary>
/// One step of a sub-agent (<see cref="ToolTaskDetail.Steps"/>).
/// </summary>
public class ToolTaskStep
{
    /// <summary>Text step.</summary>
    public const string TextType = "text";

    /// <summary>Tool-call step.</summary>
    public const string ToolType = "tool";

    /// <summary><c>text</c> or <c>tool</c>.</summary>
    public string Type { get; set; } = TextType;

    /// <summary>The text the sub-agent wrote (a text step).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>The <see cref="ToolCallDetail.Id"/> of the call (a tool step).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

/// <summary>
/// One block of a tool result.
/// </summary>
public class ToolResultBlockDto
{
    /// <summary>Block type: <c>text</c>, <c>image</c> or <c>file</c> (a file the tool handed over, by name and URI).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Text of a text block.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>Media type of an image block, e.g. <c>image/png</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    /// <summary>
    /// The stored image, readable by the thread's owner at <c>GET threads/{threadId}/attachments/{attachmentId}</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AttachmentId { get; set; }

    /// <summary>Name of a file block, e.g. <c>report.pdf</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// URI of a file block, as the tool gave it. What it links to in a chat UI is the consumer's call (its own storage URL scheme).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }
}
