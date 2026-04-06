namespace Tnzi.AI.Workspace;

/// <summary>
/// Parsed agent definition from AGENT.md workspace file
/// </summary>
public record WorkspaceAgentDefinition
{
    /// <summary>
    /// Agent identifier (directory name)
    /// </summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>
    /// Agent display name (from frontmatter 'name' field)
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Agent description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// AI provider name (e.g. "OpenAI", "Anthropic")
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Model identifier (e.g. "gpt-4o", "claude-sonnet-4-20250514")
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Sampling temperature
    /// </summary>
    public float? Temperature { get; init; }

    /// <summary>
    /// Maximum output tokens
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Tool group names to enable
    /// </summary>
    public List<string>? ToolGroups { get; init; }

    /// <summary>
    /// Execution mode (e.g. "Single", "Handoff", "AgentAsTools")
    /// </summary>
    public string? ExecutionMode { get; init; }

    /// <summary>
    /// Domain tags for agent routing
    /// </summary>
    public List<string>? Domains { get; init; }

    /// <summary>
    /// Role tags for agent routing
    /// </summary>
    public List<string>? Roles { get; init; }

    /// <summary>
    /// Quality tier (1-5)
    /// </summary>
    public int? QualityTier { get; init; }

    /// <summary>
    /// System instructions (markdown body after frontmatter)
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Persona content from PERSONA.md (if present)
    /// </summary>
    public string? PersonaContent { get; init; }

    /// <summary>
    /// Absolute file path of the AGENT.md file
    /// </summary>
    public string FilePath { get; init; } = string.Empty;
}

/// <summary>
/// Parsed persona definition from PERSONA.md workspace file
/// </summary>
public record WorkspacePersonaDefinition
{
    /// <summary>
    /// Persona name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Communication tone (e.g. "friendly", "professional")
    /// </summary>
    public string? Tone { get; init; }

    /// <summary>
    /// Preferred language
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Persona content (markdown body after frontmatter)
    /// </summary>
    public string Content { get; init; } = string.Empty;
}
