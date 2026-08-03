namespace Tnzi.AI.Cli.Controllers;

/// <summary>
/// 用户端外部执行入口：派一个任务给绑定了外部 CLI 的 Agent。
/// </summary>
/// <remarks>
/// <para>
/// 入队即返回 runId，执行在后台。外部 agent 的任务量级是「几分钟到几小时」，
/// 挂在 HTTP 请求生命周期上必然被网关超时切断，而那时子进程还在跑。
/// </para>
/// <para>
/// 权限走 Agent 自身的执行门（<c>ai.agent.execute</c>）：能运行这个 Agent 的人
/// 就能派任务给它，与它底层走内建还是外部无关 —— 那是部署配置，不是用户的权限维度。
/// </para>
/// </remarks>
[DefaultController]
[Route("ai/cli-runs")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize(PermissionName = "ai.agent.execute")]
public class DefaultCliRunController : ApiControllerBase
{
    /// <summary>外部 agent 调度器。</summary>
    protected readonly ICliAgentDispatcher Dispatcher;

    /// <summary>初始化控制器。</summary>
    public DefaultCliRunController(ICliAgentDispatcher dispatcher)
    {
        Dispatcher = Check.NotNull(dispatcher);
    }

    /// <summary>入队一次外部执行，立即返回 runId。</summary>
    [HttpPost]
    public virtual async Task<ApiResult<Guid>> Enqueue(
        [FromBody] CliRunRequestDto request, CancellationToken ct = default)
    {
        Check.NotNull(request);
        request.UserId = CurrentUser?.Id;
        return (await Dispatcher.EnqueueAsync(request, ct)).ToApiResult();
    }

    /// <summary>查询自己派出的一次运行。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<CliRunDto>> Get(Guid id, CancellationToken ct = default)
        => (await Dispatcher.GetAsync(id, ct)).ToApiResult();

    /// <summary>订阅实时事件流（SSE）。</summary>
    [HttpGet("{id:guid}/stream")]
    public virtual async Task Stream(Guid id, [FromQuery] int fromSequence = 0, CancellationToken ct = default)
    {
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        StreamingResponseWriter.ConfigureResponse(Response, format);

        await foreach (var evt in Dispatcher.StreamAsync(id, fromSequence, ct).WithCancellation(ct))
        {
            await StreamingResponseWriter.WriteEventAsync(Response, evt, format, ct);
        }
    }

    /// <summary>取消自己派出的一次运行。</summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id, CancellationToken ct = default)
        => (await Dispatcher.CancelAsync(id, ct)).ToApiResult();
}
