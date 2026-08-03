namespace Tnzi.AI.Engine;

/// <summary>
/// Separates an agent's deliverable (the answer) from its narration ("let me check the logs")
/// while a streaming turn is being consumed.
/// </summary>
/// <remarks>
/// Only the streaming path needs this. The non-streaming path's response is already just the
/// last model response, so narration from earlier tool-call rounds never enters it; streaming
/// concatenates every text delta of the whole turn, so it does.
/// <para>
/// <b>A tool call is the only boundary available.</b> The model emits narration and answer as
/// the same kind of text delta - nothing marks one as final. So: text before a tool call is
/// narration, text after the last tool call is the deliverable. This is a heuristic, and it is
/// deliberately biased towards saying too much rather than too little (see <see cref="Resolve"/>).
/// </para>
/// </remarks>
internal sealed class DeliverableTracker
{
    private readonly StringBuilder _current = new();
    private string? _previousBlock;

    /// <summary>
    /// Feed the next chunk of a streaming turn, in order.
    /// </summary>
    public void Observe(AgentStreamChunk chunk)
    {
        Check.NotNull(chunk);

        if (chunk.IsToolCall || chunk.ToolCalls != null)
        {
            if (_current.Length > 0)
            {
                _previousBlock = _current.ToString();
                _current.Clear();
            }
        }

        if (!string.IsNullOrEmpty(chunk.Text))
            _current.Append(chunk.Text);
    }

    /// <summary>
    /// Resolve the deliverable for the finished turn, or <c>null</c> when it is the whole text.
    /// </summary>
    /// <param name="fullText">The full accumulated response of the turn.</param>
    /// <remarks>
    /// Returning null when the deliverable equals the full text keeps the common case (no tool
    /// calls at all) from storing the same string twice, and lets consumers read
    /// <c>Deliverable ?? Response</c> without having to know which path produced the result.
    /// <para>
    /// When a turn ends on a tool call with nothing said afterwards, the previous text block is
    /// used instead of an empty string: an empty deliverable would render as a blank outbound
    /// message, which is far worse than one extra sentence of narration.
    /// </para>
    /// </remarks>
    public string? Resolve(string fullText)
    {
        var deliverable = _current.Length > 0 ? _current.ToString() : _previousBlock;

        return deliverable == fullText ? null : deliverable;
    }
}
