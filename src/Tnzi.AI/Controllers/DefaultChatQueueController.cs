namespace Tnzi.AI.Controllers;

/// <summary>
/// 线程的消息队列：一轮还在跑时写下的消息在服务端排队，那一轮结束时合成一轮发出。只对线程主人开放，所有方法支持重写。
/// </summary>
/// <remarks>
/// 与 <see cref="DefaultChatController"/> 分开立：模板 <c>chat/threads/{threadId}/queue</c> 不与它的任何一个重合，
/// 消费方整体替换聊天控制器时队列端点不会跟着消失。
/// </remarks>
[DefaultController]
[Route("chat/threads/{threadId:guid}/queue")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultChatQueueController : ApiControllerBase
{
    protected readonly IChatQueueService QueueService;

    public DefaultChatQueueController(IChatQueueService queueService)
    {
        QueueService = Check.NotNull(queueService);
    }

    /// <summary>线程里排队的消息，按发出的先后。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<QueuedMessageDto>>> GetList(Guid threadId, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QueueService.GetListAsync(threadId, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>排一条消息：等线程上正在跑的一轮结束；没有在跑的一轮时当场发出。</summary>
    [HttpPost]
    public virtual async Task<ApiResult<QueuedMessageDto>> Add(
        Guid threadId, [FromBody] QueueMessageRequestDto input, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QueueService.AddAsync(threadId, userId, input, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>改一条排队消息的正文。</summary>
    [HttpPut("{messageId:guid}")]
    public virtual async Task<ApiResult<QueuedMessageDto>> Update(
        Guid threadId, Guid messageId, [FromBody] UpdateQueuedMessageDto input, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QueueService.UpdateAsync(threadId, messageId, userId, input, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>删掉一条排队消息。</summary>
    [HttpDelete("{messageId:guid}")]
    public virtual async Task<ApiResult> Remove(Guid threadId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QueueService.RemoveAsync(threadId, messageId, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>立即发送：停掉线程上正在跑的一轮，这条消息单独作为下一轮发出。</summary>
    [HttpPost("{messageId:guid}/send-now")]
    public virtual async Task<ApiResult<QueuedMessageDto>> SendNow(Guid threadId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QueueService.SendNowAsync(threadId, messageId, userId, cancellationToken);
        return result.ToApiResult();
    }
}
