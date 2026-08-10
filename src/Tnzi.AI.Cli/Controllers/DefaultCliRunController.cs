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
/// <para>
/// ★ <b>但那个码只回答「能不能派」，不回答「能不能看别人派的」</b>。本控制器与管理端控制器
/// 调用的是<b>同一批</b>调度器方法，归属判定因此落在 <c>CliAgentDispatcher.CanSeeAsync</c>
/// （服务层，因为这个类是 <c>[DefaultController]</c>、可被消费应用整体替换）：
/// 派出者按 <c>CreatorId</c> 放行、管理码 <c>ai.cliRun.view</c> 放行全部、其余一律 404。
/// 下面几个方法注释里说的「自己」指的就是那一条判定，不是靠调用方自觉。
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
