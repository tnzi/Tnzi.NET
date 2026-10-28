namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 默认心跳：每隔 <see cref="Interval"/> 把运行行的 <see cref="AgentRun.LastHeartbeatAt"/> 推到当前时刻。
/// </summary>
/// <remarks>
/// <para>
/// 每次心跳在<b>自己的作用域</b>里执行：心跳循环与运行本身并发，而 DbContext 不允许并发使用。
/// 用集合更新（<c>ExecuteUpdate</c>）只写这一列，不经变更跟踪：运行自己的作用域里还拿着这一行的实体，
/// 收尾时按实体整行写回，两者互不干扰（整行写回会把心跳列回退到实体里的旧值，下一次心跳就补上）。
/// </para>
/// <para>
/// 按主键更新时摘掉全局过滤器：心跳循环里未必有当前租户，过滤器会把租户的行藏起来，于是 UPDATE 0 行、零症状。
/// 运行 Id 是随机 Guid，按它更新一列时间戳没有跨租户面；软删条件手工补回。
/// </para>
/// </remarks>
public sealed class AgentRunHeartbeat : IAgentRunHeartbeat
{
    /// <summary>默认心跳间隔。</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunHeartbeat> _logger;

    public AgentRunHeartbeat(IServiceScopeFactory scopeFactory, ILogger<AgentRunHeartbeat> logger)
        : this(scopeFactory, logger, DefaultInterval)
    {
    }

    internal AgentRunHeartbeat(IServiceScopeFactory scopeFactory, ILogger<AgentRunHeartbeat> logger, TimeSpan interval)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        Interval = interval > TimeSpan.Zero ? interval : DefaultInterval;
    }

    /// <summary>心跳间隔。</summary>
    public TimeSpan Interval { get; }

    /// <inheritdoc />
    public IAsyncDisposable Start(Guid runId) => new Lease(this, runId);

    /// <summary>刷新一次心跳。失败只记日志：心跳断几次不影响运行本身，回收按多倍间隔判定。</summary>
    internal async Task BeatAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<AgentRun, Guid>>();
            var now = DateTime.UtcNow;

            await repository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(r => r.Id == runId && !r.IsDeleted
                            && (r.Status == AgentRunStatus.Pending || r.Status == AgentRunStatus.Running))
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastHeartbeatAt, now), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record a heartbeat for agent run {RunId}", runId);
        }
    }

    private async Task LoopAsync(Guid runId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await BeatAsync(runId, cancellationToken);
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public Lease(AgentRunHeartbeat owner, Guid runId)
        {
            // 心跳不该带着运行的环境状态（当前请求、属性包）跑在后台
            using (ExecutionContext.SuppressFlow())
            {
                _loop = Task.Run(() => owner.LoopAsync(runId, _stop.Token));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }
}
