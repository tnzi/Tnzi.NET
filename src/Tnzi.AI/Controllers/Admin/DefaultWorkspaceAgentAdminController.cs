namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Read-only admin endpoints for workspace-discovered Agent + Persona
/// definitions. Sits next to the editable <c>admin/agents</c> CRUD
/// (<see cref="DefaultAgentAdminController"/>); see
/// <see cref="IWorkspaceAgentAdminService"/> for why the two are kept
/// separate (different identity types, different lifecycles).
/// </summary>
[Route("admin/ai/workspace")]
[DefaultController]
[ApiAuthorize(PermissionName = "ai.agent.view")]
public class DefaultWorkspaceAgentAdminController : ApiAdminControllerBase
{
    protected readonly IWorkspaceAgentAdminService Service;

    public DefaultWorkspaceAgentAdminController(IWorkspaceAgentAdminService service)
    {
        Service = Check.NotNull(service);
    }

    /// <summary>List every workspace-discovered Agent definition.</summary>
    [HttpGet("agents")]
    public virtual async Task<ApiResult<List<WorkspaceAgentDto>>> ListAgents(CancellationToken cancellationToken = default)
    {
        var result = await Service.ListAgentsAsync(cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>List every workspace-discovered Persona definition.</summary>
    [HttpGet("personas")]
    public virtual async Task<ApiResult<List<WorkspacePersonaDto>>> ListPersonas(CancellationToken cancellationToken = default)
    {
        var result = await Service.ListPersonasAsync(cancellationToken);
        return result.ToApiResult();
    }
}
