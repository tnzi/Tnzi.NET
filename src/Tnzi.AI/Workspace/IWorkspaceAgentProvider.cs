namespace Tnzi.AI.Workspace;

/// <summary>
/// Discovers and loads workspace-based agent definitions from AGENT.md files
/// </summary>
[ExperimentalApi(Reason = "Gateway/Device/Workspace API under active development")]
public interface IWorkspaceAgentProvider
{
    /// <summary>
    /// Discover all agent definitions under the given workspace path.
    /// Scans subdirectories for AGENT.md files.
    /// </summary>
    /// <param name="workspacePath">Root directory to scan</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of discovered agent definitions</returns>
    Task<IReadOnlyList<WorkspaceAgentDefinition>> DiscoverAsync(string workspacePath, CancellationToken ct = default);

    /// <summary>
    /// Load a specific agent definition by agent ID.
    /// Looks for {workspacePath}/{agentId}/AGENT.md.
    /// </summary>
    /// <param name="workspacePath">Root directory containing agent folders</param>
    /// <param name="agentId">Agent identifier (directory name)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent definition, or null if not found</returns>
    Task<WorkspaceAgentDefinition?> LoadAsync(string workspacePath, string agentId, CancellationToken ct = default);
}
