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
    /// Whether the admin listing also scans the project's .agents/ directory (under the content root).
    /// Runtime fallback only reads <see cref="GlobalPath"/>/&lt;agent guid&gt;/AGENT.md.
    /// </summary>
    /// <remarks>Definitions are read from disk on every listing and resolution, so edits apply on the next request.</remarks>
    public bool ScanProjectPath { get; set; } = true;
}
