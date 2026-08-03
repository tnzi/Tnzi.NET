// R3：二级目录（Controllers/Admin/）不产生子命名空间。
namespace Tnzi.AI.Cli.Controllers;

/// <summary>
/// 外部执行记录的查询、实时订阅与取消。
/// </summary>
[DefaultController]
[Route("admin/ai/cli-runs")]
[ApiAuthorize(PermissionName = "ai.cliRun.view")]
public class DefaultCliRunAdminController : ApiAdminControllerBase
{
    /// <summary>外部 agent 调度器。</summary>
    protected readonly ICliAgentDispatcher Dispatcher;

    /// <summary>初始化控制器。</summary>
    public DefaultCliRunAdminController(ICliAgentDispatcher dispatcher)
    {
        Dispatcher = Check.NotNull(dispatcher);
    }

    /// <summary>分页查询运行记录。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<CliRunDto>>> GetList(
        [FromQuery] CliRunQueryDto query, CancellationToken ct = default)
        => (await Dispatcher.GetListAsync(query, ct)).ToApiResult();

    /// <summary>取单条运行记录。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<CliRunDto>> Get(Guid id, CancellationToken ct = default)
        => (await Dispatcher.GetAsync(id, ct)).ToApiResult();

    /// <summary>取历史事件（详情页回放）。</summary>
    [HttpGet("{id:guid}/messages")]
    public virtual async Task<ApiResult<List<CliRunMessageDto>>> GetMessages(
        Guid id, [FromQuery] int fromSequence = 0, CancellationToken ct = default)
        => (await Dispatcher.GetMessagesAsync(id, fromSequence, ct)).ToApiResult();

    /// <summary>
    /// 订阅实时事件流（SSE）。
    /// </summary>
    /// <remarks>
    /// <paramref name="fromSequence"/> 让断线重连能<b>精确</b>补发：客户端记住最后收到的
    /// 序号，重连时带上，服务端从那之后接着发。不这样做的话，一次网络抖动要么丢事件，
    /// 要么重复渲染已经显示过的内容。
    /// </remarks>
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

    /// <summary>取消一次运行。已在跑的会整树终止子进程。</summary>
    [HttpPost("{id:guid}/cancel")]
    [ApiAuthorize(PermissionName = "ai.cliRun.execute")]
    public virtual async Task<ApiResult> Cancel(Guid id, CancellationToken ct = default)
        => (await Dispatcher.CancelAsync(id, ct)).ToApiResult();
}
