namespace Tnzi.AI.Options;

/// <summary>
/// Workspace-based agent discovery configuration
/// </summary>
public class WorkspaceOptions
{
    /// <summary>
    /// Whether workspace agent discovery is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Global workspace path for agent definitions.
    /// Defaults to ~/.tnzi/agents/
    /// </summary>
    public string GlobalPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tnzi", "agents");

    /// <summary>
    /// Whether to also scan the current project path for .agents/ directory
    /// </summary>
    public bool ScanProjectPath { get; set; } = true;

    /// <summary>
    /// Whether to enable file watching for hot reload of agent definitions
    /// </summary>
    public bool FileWatchEnabled { get; set; }
}
