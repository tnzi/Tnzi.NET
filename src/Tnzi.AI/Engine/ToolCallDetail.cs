
namespace Tnzi.AI.Engine;

/// <summary>
/// Tool call execution detail (name + duration, for client-side benchmarking)
/// </summary>
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
}
