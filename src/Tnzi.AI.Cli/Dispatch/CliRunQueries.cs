namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 后台作用域里对 <see cref="CliRun"/> 的查询入口。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么后台不能直接用 <c>AsQueryable()</c></b>：<see cref="CliRun"/> 是
/// <c>MultiTenantAuditedEntity</c>，多租户一开，全局查询过滤器就是
/// <c>TenantId == 当前租户</c>（严格等值，没有 host / global 租户）。而认领、续租、回收、
/// 执行器的加载、MCP 回写凭据的校验全部发生在<b>没有 HTTP 请求、没有 <c>ICurrentTenant</c></b>
/// 的作用域里 —— 当前租户恒为 null，过滤器退化成 <c>TenantId IS NULL</c>。
/// 于是租户用户入队得到 200 和一个 runId，行上带着 TenantId，然后**永远停在 Queued**：
/// 认领查询看不见它，没有任何错误日志，SSE 一条事件都不发。
/// </para>
/// <para>
/// 这一层的查询按设计就是跨租户的：队列服务的是所有租户。所以这里显式摘掉全局过滤器，
/// 再把软删条件<b>手工补回来</b>（<c>IgnoreQueryFilters</c> 是整体摘除，软删过滤器会一起没掉）。
/// 刻意不用 <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;</c>：它对每次调用记一条 Warning
/// 作跨租户审计，而认领循环每个轮询周期都要查一次 —— 那会把日志刷成噪音，等真有人越权时反而没人看。
/// </para>
/// <para>
/// 租户隔离在这一层<b>不是靠过滤器</b>而是靠数据：认领到的运行随身带着 <c>TenantId</c>，
/// 执行器加载后用 <c>ICurrentTenant.Change(run.TenantId)</c> 切到它的租户再解析绑定 / 运行时 / Agent，
/// 事件行也显式写 <c>run.TenantId</c>。
/// </para>
/// </remarks>
internal static class CliRunQueries
{
    /// <summary>
    /// 跨租户地查运行表（软删过滤仍然生效）。只供后台作用域使用；
    /// 请求路径上的读取（<c>CliAgentDispatcher</c>）照常走带租户过滤的 <c>AsQueryable()</c>。
    /// </summary>
    public static IQueryable<CliRun> AcrossTenants(this IRepository<CliRun, Guid> repository, bool withTracking = false)
    {
        Check.NotNull(repository);
        return repository.AsQueryable(withTracking)
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted);
    }
}
