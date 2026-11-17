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

    protected readonly IOptionsMonitor<AiChatOptions>? ChatOptions;

    public DefaultChatModelController(IUsagePolicyService usagePolicy, IOptionsMonitor<AiChatOptions>? chatOptions = null)
    {
        UsagePolicy = Check.NotNull(usagePolicy);
        ChatOptions = chatOptions;
    }

    /// <summary>
    /// 当前用户在这个 Agent 上可以选的模型与默认选中的那个。多于一个时聊天界面显示选择器；没配模型清单时为空列表。
    /// </summary>
    /// <param name="agentId">聊天的 Agent；不带 Agent 的聊天不传，按 <paramref name="provider"/>（或默认 provider）过滤。</param>
    /// <param name="provider">不带 Agent 的聊天用的 provider。</param>
    /// <param name="threadId">
    /// 对话线程（可选）：应用注册了 <c>IChatModelTargetResolver</c> 时，按这段对话实际的 provider / 模型列出（例如用户档案把它换到了别家 CLI）。
    /// 不是这个用户的线程按没给处理。
    /// </param>
    /// <param name="metadata">
    /// 页面将随下一轮发出的每轮元数据（JSON 对象文本，与 <c>ChatRequestDto.Metadata</c> 同一上限，不合规答 400 <c>AI_INVALID_TURN_METADATA</c>）：
    /// 交给应用的 <c>IChatModelTargetResolver</c>，例如用户在输入框旁选的 CLI 档案，好让还没有线程的新对话也按那家列出模型与档位。
    /// 这一轮校验模型与强度时用的是请求里同一份元数据。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet]
    public virtual async Task<ApiResult<ChatModelsDto>> GetModels(
        [FromQuery] Guid? agentId = null, [FromQuery] string? provider = null, [FromQuery] Guid? threadId = null,
        [FromQuery] string? metadata = null, CancellationToken ct = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var turnMetadata = ChatTurnMetadata.ValidateJson(metadata, ChatOptions?.CurrentValue.Metadata ?? new ChatMetadataOptions());
        if (!turnMetadata.Succeeded)
        {
            return Result<ChatModelsDto>.Failure(turnMetadata.Message!, turnMetadata.Code ?? 400, turnMetadata.ErrorCode).ToApiResult();
        }

        var result = await UsagePolicy.GetChatModelsAsync(
            new ChatModelsRequest(userId, agentId, provider, threadId) { Metadata = turnMetadata.Data }, ct);
        return result.ToApiResult();
    }
}
