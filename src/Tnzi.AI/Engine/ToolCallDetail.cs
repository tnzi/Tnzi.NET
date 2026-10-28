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

    /// <summary>A copy that shares nothing mutable with this instance.</summary>
    public ToolCallDetail Copy()
    {
        var copy = (ToolCallDetail)MemberwiseClone();
        copy.Result = Result?.ToList();
        return copy;
    }
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
