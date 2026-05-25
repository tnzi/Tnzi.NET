namespace Tnzi.AI.Dtos;

/// <summary>
/// Admin-facing read-only view of a workspace-discovered Agent definition.
/// Mirrors <see cref="Tnzi.AI.Workspace.WorkspaceAgentDefinition"/> with
/// `Source` + `IsReadOnly` provenance fields added so the admin UI's
/// `TSourceBadge` + `TRowActions` infrastructure can render it uniformly.
///
/// These rows are never editable through the admin — they're materialised
/// from `AGENT.md` files on disk (under <c>~/.tnzi/agents/</c> globally or
/// the project's <c>.agents/</c> directory). Edits should happen by
/// modifying the file on disk; the file watcher (when enabled) hot-reloads.
/// </summary>
public class WorkspaceAgentDto
{
    /// <summary>Agent identifier (directory name).</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Display name from the frontmatter `name` field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description from frontmatter.</summary>
    public string? Description { get; set; }

    /// <summary>AI provider name (e.g. OpenAI / Anthropic).</summary>
    public string? Provider { get; set; }

    /// <summary>Model identifier.</summary>
    public string? Model { get; set; }

    /// <summary>Tool group names enabled by this agent.</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Execution mode (Single / Handoff / AgentAsTools).</summary>
    public string? ExecutionMode { get; set; }

    /// <summary>Domain tags.</summary>
    public List<string>? Domains { get; set; }

    /// <summary>Role tags.</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Quality tier (1-5).</summary>
    public int? QualityTier { get; set; }

    /// <summary>Absolute file path of the AGENT.md.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Where the file was found — "Global" (~/.tnzi/agents/) or "Project" (.agents/).</summary>
    public string WorkspaceScope { get; set; } = "Global";

    /// <summary>True when an accompanying PERSONA.md exists.</summary>
    public bool HasPersona { get; set; }

    /// <summary>
    /// System instructions extracted from the AGENT.md markdown body
    /// (after the YAML frontmatter). Surfaced on the list response since
    /// workspace agent counts are bounded (typically 10s, not 1000s) and
    /// the admin UI wants to preview the full prompt inline.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Persona content from PERSONA.md, if a sibling file exists in the
    /// same agent folder. Surfaced inline for the same reason as
    /// `Instructions`.
    /// </summary>
    public string? PersonaContent { get; set; }

    /// <summary>Discriminator for the front-end `TSourceBadge`.</summary>
    public string Source { get; set; } = "Workspace";

    /// <summary>Always true — workspace agents are file-backed and read-only.</summary>
    public bool IsReadOnly { get; set; } = true;
}

/// <summary>
/// Admin-facing read-only view of a workspace-discovered Persona definition.
/// Sourced from PERSONA.md files alongside AGENT.md.
/// </summary>
public class WorkspacePersonaDto
{
    /// <summary>Persona name from frontmatter.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Communication tone (friendly / professional / …).</summary>
    public string? Tone { get; set; }

    /// <summary>Preferred language tag.</summary>
    public string? Language { get; set; }

    /// <summary>Owning agent's id (PERSONA.md lives inside the agent's folder).</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Absolute file path of the PERSONA.md.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Where the file was found — "Global" or "Project".</summary>
    public string WorkspaceScope { get; set; } = "Global";

    /// <summary>Discriminator for the front-end `TSourceBadge`.</summary>
    public string Source { get; set; } = "Workspace";

    /// <summary>Always true.</summary>
    public bool IsReadOnly { get; set; } = true;
}
