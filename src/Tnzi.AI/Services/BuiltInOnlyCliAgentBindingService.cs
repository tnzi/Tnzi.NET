namespace Tnzi.AI.Services;

/// <summary>
/// 未加载 <c>Tnzi.AI.Cli</c> 时的绑定服务：<b>所有 Agent 一律走内建执行</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>查询返回 <c>null</c> 而不是 501</b>：<see cref="GetByAgentIdAsync"/> 是
/// <see cref="IAgentDispatchFacade"/> 的路由判据，「没装外部执行能力」的正确答案就是
/// 「全部走内建」。若这里抛 501，未加载子模块的部署会连普通聊天都跑不起来。
/// 写操作（新建/删除绑定）仍返回 501，因为那确实做不到。
/// </para>
/// <para>
/// 因此它刻意<b>不</b>实现 <see cref="INoOpService"/>，命名也不是 <c>NoOp*</c> ——
/// 那个标记的语义是「降级到 501 的占位实现」，会被 AIModule 的启动探测报成降级。
/// 而本类在读路径上给出的是<b>正确答案</b>，不是降级；每次启动多打一行「回退中」只会制造噪音。
/// </para>
/// </remarks>
public class BuiltInOnlyCliAgentBindingService : ICliAgentBindingService
{
    private const string Message =
        "External CLI agent bindings require the Tnzi.AI.Cli module. "
        + "Add [DependsOn(typeof(AICliModule))] and set AI:Cli:Enabled=true to enable it.";

    /// <inheritdoc />
    public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<CliAgentBindingDto?>(null);

    /// <inheritdoc />
    public Task<Result<CliAgentBindingDto>> UpsertAsync(
        Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CliAgentBindingDto>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.CliModuleNotLoaded));
}
