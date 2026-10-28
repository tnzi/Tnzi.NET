namespace Tnzi.AI.Services;

/// <summary>
/// 聊天服务接口
/// </summary>
public interface IChatService
{
    /// <summary>
    /// 发送消息
    /// </summary>
    Task<Result<ChatResponseDto>> ChatAsync(ChatRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// 流式发送消息（delta 模型 - 每个事件只包含增量内容）
    /// </summary>
    IAsyncEnumerable<StreamEvent> ChatStreamingAsync(ChatRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// 线程上还没结束的运行（外部 CLI 运行；内建运行绑在请求上，请求走了它也就停了）。刷新之后客户端据此重新接上。
    /// 线程不存在或不是 <paramref name="userId"/> 的时 404。
    /// </summary>
    Task<Result<List<ChatRunDto>>> GetActiveRunsAsync(Guid threadId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(Result<List<ChatRunDto>>.Success([]));

    /// <summary>
    /// 重新接上一次运行的流：从头或从某个事件序号之后。事件形状与 <see cref="ChatStreamingAsync"/> 相同。
    /// 调用者看不见这次运行时以 404 的 <see cref="BusinessException"/> 结束（此时一个事件都还没写出）。
    /// </summary>
    IAsyncEnumerable<StreamEvent> AttachStreamingAsync(Guid runId, int fromSequence = 0, CancellationToken ct = default)
        => throw new BusinessException("Run not found.", ErrorCodes.ChatRunNotFound, 404);

    /// <summary>在服务端取消一次运行（不只是断开连接）。</summary>
    Task<Result> CancelRunAsync(Guid runId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound));

    /// <summary>
    /// 回答一次运行的一个权限请求（流上的 <c>permission_request</c> 事件）：只有发起这一轮的人（或持 <c>ai.cliRun.execute</c> 的人）能答，
    /// 其余 404；选项不在请求给的那些里 400；请求已有结局 409。
    /// </summary>
    Task<Result> AnswerRunPermissionAsync(Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken ct = default)
        => Task.FromResult(Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound));
}
