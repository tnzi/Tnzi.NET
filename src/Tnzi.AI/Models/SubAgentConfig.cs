namespace Tnzi.AI.Models;

public record SubAgentConfig(
    string Name,
    string Description,
    string SystemPrompt,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? DisallowedTools = null,
    string Model = "inherit",
    int MaxTurns = 50,
    TimeSpan Timeout = default)
{
    public static readonly IReadOnlyList<string> DefaultDisallowedTools = ["task", "ask_clarification", "present_files"];
}
