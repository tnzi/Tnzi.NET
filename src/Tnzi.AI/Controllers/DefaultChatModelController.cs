namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端模型选择：当前用户在某个 Agent 上可以选的模型。
/// </summary>
/// <remarks>
/// 挂在独立的路由模板 <c>chat/models</c> 上，不与 <c>DefaultChatController</c>（<c>chat</c>）共用：覆写聊天控制器的消费方不会连带把它删掉。
/// </remarks>
[DefaultController]
[Route("chat/models")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]
public class DefaultChatModelController : ApiControllerBase
{
    protected readonly IUsagePolicyService UsagePolicy;

    public DefaultChatModelController(IUsagePolicyService usagePolicy)
    {
        UsagePolicy = Check.NotNull(usagePolicy);
    }

    /// <summary>
    /// 当前用户在这个 Agent 上可以选的模型与默认选中的那个。多于一个时聊天界面显示选择器；没配模型清单时为空列表。
    /// </summary>
    /// <param name="agentId">聊天的 Agent；不带 Agent 的聊天不传，按 <paramref name="provider"/>（或默认 provider）过滤。</param>
    /// <param name="provider">不带 Agent 的聊天用的 provider。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet]
    public virtual async Task<ApiResult<ChatModelsDto>> GetModels(
        [FromQuery] Guid? agentId = null, [FromQuery] string? provider = null, CancellationToken ct = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await UsagePolicy.GetChatModelsAsync(userId, agentId, provider, ct);
        return result.ToApiResult();
    }
}
