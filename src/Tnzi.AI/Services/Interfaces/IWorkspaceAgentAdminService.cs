using Tnzi.AI.Dtos;
using Tnzi.Results;

namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Admin-facing read-only access to workspace-discovered Agent + Persona
/// definitions. Distinct from <see cref="IAgentService"/> (which manages
/// the editable database-backed <c>Agent</c> entity) because the two have
/// different identity types (string AgentId vs Guid) and lifecycles
/// (file-watched vs DB-persisted).
///
/// Powers <c>GET /admin/ai/workspace-agents</c> + <c>GET /admin/ai/workspace-personas</c>.
/// </summary>
public interface IWorkspaceAgentAdminService
{
    /// <summary>
    /// Discover every workspace agent across the configured paths
    /// (`WorkspaceOptions.GlobalPath` + the active project's `.agents/`
    /// directory when `ScanProjectPath=true`).
    /// </summary>
    Task<Result<List<WorkspaceAgentDto>>> ListAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover every workspace persona — currently surfaces the
    /// PERSONA.md attached to each AGENT.md folder. Stand-alone
    /// PERSONA.md files (without a sibling AGENT.md) are not yet
    /// surfaced because the file-watcher infrastructure doesn't expose
    /// them as first-class entities.
    /// </summary>
    Task<Result<List<WorkspacePersonaDto>>> ListPersonasAsync(CancellationToken cancellationToken = default);
}
