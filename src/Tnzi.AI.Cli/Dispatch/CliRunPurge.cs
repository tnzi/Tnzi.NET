namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 线程删除之后，删掉它的外部运行留在库里的内容：运行记录与事件流。
/// </summary>
/// <remarks>
/// <para>
/// 两张表装的就是对话本身：<see cref="CliRun"/> 上有提示词、本轮上下文、交付物与错误信息，
/// <see cref="CliRunMessage"/> 是整段事件流，含每一次工具调用的输入与输出。所以是<b>硬删</b>：
/// 软删留下的行里内容原样都在（核心对内建 agent 的 <c>AgentRun</c> 同样硬删）。
/// 用量与成本的事实来源是 <c>UsageLog</c>，不是这两张表，删掉它们不改变预算与账目。
/// </para>
/// <para>
/// 调用方有两个：线程删除事件的处理器（<c>CliThreadCleanupHandler</c>），以及执行器在一条属于已删除线程的运行
/// 开始或结束时（处理器删不了还在执行的运行，见它的说明）。全部按 Id 谓词，重复执行安全。
/// </para>
/// </remarks>
internal static class CliRunPurge
{
    /// <summary>
    /// 这条线程是否<b>确实已被删除</b>：线程行存在且已软删。
    /// </summary>
    /// <remarks>
    /// 找不到线程行时回答 false：那不是「已删除」的证据（只有软删会发布线程删除事件），据此删运行记录只会删错。
    /// 摘掉全局过滤器，否则软删的线程本身就查不到；只按主键查，不比较租户列（多租户关闭时那一列未映射）。
    /// </remarks>
    public static async Task<bool> IsThreadDeletedAsync(
        IRepository<AgentThread, Guid> threadRepository, Guid threadId, CancellationToken cancellationToken)
    {
        Check.NotNull(threadRepository);

        var isDeleted = await threadRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(t => t.Id == threadId)
            .Select(t => (bool?)t.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return isDeleted == true;
    }

    /// <summary>这条线程的全部运行，软删的也算。</summary>
    /// <remarks>
    /// 摘掉全局过滤器：软删的行里内容同样还在；调用方（事件处理器、后台执行器）也未必处在线程的租户里。
    /// 范围由线程 Id 给定。
    /// </remarks>
    public static IQueryable<CliRun> RunsOfThread(IRepository<CliRun, Guid> runRepository, Guid threadId)
    {
        Check.NotNull(runRepository);
        return runRepository.AsQueryable().IgnoreQueryFilters().Where(r => r.ThreadId == threadId);
    }

    /// <summary>删掉这些运行的事件流。</summary>
    /// <remarks>
    /// 摘掉全局过滤器：事件行带着运行的租户，而调用方（事件处理器、后台执行器）未必处在那个租户里。
    /// 范围由运行 Id 给定，Id 来自已经定位到的运行，不会越出这条线程。
    /// </remarks>
    public static async Task DeleteMessagesAsync(
        IRepository<CliRunMessage, Guid> messageRepository, IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken)
    {
        Check.NotNull(messageRepository);
        Check.NotNull(runIds);

        if (runIds.Count == 0)
        {
            return;
        }

        await messageRepository.EnsureTransactionStartedAsync(cancellationToken);
        await messageRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(m => runIds.Contains(m.RunId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>删掉这些运行的权限请求（工具名与使用方的回答说明也是对话内容）。仓储为 null（直接构造的测试）时跳过。</summary>
    public static async Task DeletePermissionRequestsAsync(
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository, IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken)
    {
        Check.NotNull(runIds);

        if (permissionRepository is null || runIds.Count == 0)
        {
            return;
        }

        await permissionRepository.EnsureTransactionStartedAsync(cancellationToken);
        await permissionRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => runIds.Contains(r.RunId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>删掉一条运行：先删事件流、权限请求与它的附件，再删运行记录。</summary>
    /// <remarks>
    /// <para>
    /// 先删事件：中途失败时留下的是一条没有事件的运行（还能按线程找到、再删一次），而不是一批找不到运行的事件。
    /// </para>
    /// <para>
    /// 附件（用户随消息发来的、工具返回的图片）按运行 id 删：线程删除处理器按线程 id 删过一遍，
    /// 但这条运行在那之后还在跑，又写下了工具图片。<paramref name="attachmentRepository"/> 为 null 时跳过（直接构造的测试）。
    /// </para>
    /// </remarks>
    public static async Task DeleteRunAsync(
        IRepository<CliRun, Guid> runRepository, IRepository<CliRunMessage, Guid> messageRepository, Guid runId,
        CancellationToken cancellationToken, IRepository<AgentAttachment, Guid>? attachmentRepository = null,
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository = null)
    {
        Check.NotNull(runRepository);

        await DeleteMessagesAsync(messageRepository, [runId], cancellationToken);
        await DeletePermissionRequestsAsync(permissionRepository, [runId], cancellationToken);

        if (attachmentRepository is not null)
        {
            await attachmentRepository.EnsureTransactionStartedAsync(cancellationToken);
            await attachmentRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(a => a.RunId == runId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await runRepository.EnsureTransactionStartedAsync(cancellationToken);
        await runRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => r.Id == runId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
