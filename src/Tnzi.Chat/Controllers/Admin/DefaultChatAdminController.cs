namespace Tnzi.Chat.Controllers.Admin;

[DefaultController]
[Route("admin/chat")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultChatAdminController : ApiAdminControllerBase
{
    protected readonly IBroadcastService Broadcast;

    public DefaultChatAdminController(IBroadcastService broadcast)
    {
        Broadcast = Check.NotNull(broadcast);
    }

    /// <summary>Broadcast a system notification to roles and/or users.</summary>
    [HttpPost("broadcast")]
    public virtual async Task<ApiResult<int>> SendBroadcast([FromBody] BroadcastDto input)
        => (await Broadcast.BroadcastAsync(input)).ToApiResult();
}
