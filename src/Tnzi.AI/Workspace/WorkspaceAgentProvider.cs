namespace Tnzi.AI.Workspace;

/// <summary>
/// File-system based workspace agent provider.
/// Discovers and loads agent definitions from AGENT.md files in workspace directories.
/// </summary>
public class WorkspaceAgentProvider : IWorkspaceAgentProvider
{
    private const string AgentFileName = "AGENT.md";
    private const string PersonaFileName = "PERSONA.md";

    private readonly ILogger<WorkspaceAgentProvider> _logger;

    public WorkspaceAgentProvider(ILogger<WorkspaceAgentProvider> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceAgentDefinition>> DiscoverAsync(string workspacePath, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(workspacePath);

        if (!Directory.Exists(workspacePath))
        {
            _logger.LogDebug("Workspace path does not exist: {Path}", workspacePath);
            return [];
        }

        var results = new List<WorkspaceAgentDefinition>();

        try
        {
            var directories = Directory.GetDirectories(workspacePath);
            foreach (var dir in directories)
            {
                ct.ThrowIfCancellationRequested();

                var agentFile = Path.Combine(dir, AgentFileName);
                if (!File.Exists(agentFile)) continue;

                var agentId = Path.GetFileName(dir);
                var definition = await LoadAgentFromFileAsync(agentFile, agentId, ct);
                if (definition != null)
                {
                    results.Add(definition);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover workspace agents from {Path}", workspacePath);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<WorkspaceAgentDefinition?> LoadAsync(string workspacePath, string agentId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(workspacePath);
        Check.NotNullOrWhiteSpace(agentId);

        var agentDir = Path.Combine(workspacePath, agentId);
        var agentFile = Path.Combine(agentDir, AgentFileName);

        if (!File.Exists(agentFile))
        {
            _logger.LogDebug("Agent file not found: {Path}", agentFile);
            return null;
        }

        var definition = await LoadAgentFromFileAsync(agentFile, agentId, ct);
        if (definition == null)
        {
            return null;
        }

        // Check for PERSONA.md and merge content
        var personaFile = Path.Combine(agentDir, PersonaFileName);
        if (File.Exists(personaFile))
        {
            try
            {
                var personaContent = await File.ReadAllTextAsync(personaFile, ct);
                var persona = AgentMarkdownParser.ParsePersona(personaContent);
                if (persona != null)
                {
                    // Return new definition with merged persona content (immutable record)
                    definition = definition with { PersonaContent = persona.Content };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse persona file: {Path}", personaFile);
            }
        }

        return definition;
    }

    /// <summary>
    /// Load and parse an AGENT.md file
    /// </summary>
    private async Task<WorkspaceAgentDefinition?> LoadAgentFromFileAsync(string filePath, string agentId, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var definition = AgentMarkdownParser.Parse(content, agentId);
            if (definition == null)
            {
                _logger.LogWarning("Failed to parse agent definition from {Path}: missing frontmatter or name", filePath);
                return null;
            }

            // Set file path on the definition (immutable record)
            return definition with { FilePath = filePath };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agent file: {Path}", filePath);
            return null;
        }
    }
}
