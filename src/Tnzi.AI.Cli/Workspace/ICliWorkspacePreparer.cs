namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 为一次外部执行布置独立的工作区。
/// </summary>
/// <remarks>
/// <para>
/// ★ 「独立」指目录各归各的（续接、回收、回滚都靠它），<b>不是权限边界</b>：本机宿主上所有外部运行以 API 进程这一个 OS 账号执行，
/// 子进程能读写该账号可达的一切，包括别的租户 / 用户的工作区与在途运行的回写凭据。需要运行之间互相隔离的部署
/// 要在宿主层面分开：提供按用户交出独立目标（容器 / OS 账号）的 <see cref="ICliHost"/>。
/// </para>
/// <para>
/// 工作区布置在运行的执行目标上（<see cref="ICliHostTarget"/>）：文件经它的文件系统写，路径按它的路径语法拼。
/// </para>
/// <para>
/// <b>上下文靠「布置工作目录」投递，而不是靠「拼 prompt」</b> —— 这是外部 agent 与内建
/// agent 最大的形态差异。每个编码 CLI 都会自己读工作目录里的记忆文件、技能目录、
/// 项目配置；把同样的内容塞进 prompt 只会重复占用上下文窗口，还绕过了它们各自的缓存机制。
/// </para>
/// </remarks>
public interface ICliWorkspacePreparer
{
    /// <summary>为一次运行布置工作区：目录树 + brief + skills + 上下文 sidecar。</summary>
    Task<CliWorkspace> PrepareAsync(ICliHostTarget target, CliRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 复用既有工作目录并在它原来的运行根上刷新上下文文件，归属（是否用户目录）保持不变。
    /// 目录不存在、或认不出是按当前绑定布置的工作目录时返回 null，调用方回落到 <see cref="PrepareAsync"/>。
    /// </summary>
    Task<CliWorkspace?> ReuseAsync(
        ICliHostTarget target, string workDirectory, CliRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 认出一个正被进程使用的工作区，<b>什么都不写</b>：接回一个服务端重启前起的进程时用，好在运行结束时照常收尾。
    /// 认不出来（目录不在、不是按当前绑定布置的、没有清单）时返回 null，收尾交给回收器。
    /// </summary>
    /// <remarks>
    /// 不能用 <see cref="ReuseAsync"/>：它会重写受管文件，进程正在读的 MCP 配置里的回写凭据会被换成一份新签的，
    /// 进程手里那份随即失效。缺省返回 null，自定义布置器不实现它也照样能接回进程，只是那一轮的受管文件要等回收器清。
    /// </remarks>
    Task<CliWorkspace?> OpenAsync(
        ICliHostTarget target, string workDirectory, CliRunContext context, CancellationToken cancellationToken)
        => Task.FromResult<CliWorkspace?>(null);

    /// <summary>
    /// 按 sidecar 清单精确回滚本次写入的文件/目录。
    /// </summary>
    /// <param name="target">工作区所在的执行目标（布置它的那一个）。</param>
    /// <param name="workspace">要清理的工作区。</param>
    /// <param name="removeAll">
    /// true = 连同隔离根目录一起删。<b>用户提供的工作目录永远不会被删</b>，
    /// 无论这个参数是什么。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CleanupAsync(ICliHostTarget target, CliWorkspace workspace, bool removeAll, CancellationToken cancellationToken);
}
