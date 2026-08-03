namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 为一次外部执行布置隔离工作区。
/// </summary>
/// <remarks>
/// <b>上下文靠「布置工作目录」投递，而不是靠「拼 prompt」</b> —— 这是外部 agent 与内建
/// agent 最大的形态差异。每个编码 CLI 都会自己读工作目录里的记忆文件、技能目录、
/// 项目配置；把同样的内容塞进 prompt 只会重复占用上下文窗口，还绕过了它们各自的缓存机制。
/// </remarks>
public interface ICliWorkspacePreparer
{
    /// <summary>为一次运行布置工作区：目录树 + brief + skills + 上下文 sidecar。</summary>
    Task<CliWorkspace> PrepareAsync(CliRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 复用既有工作目录并刷新上下文文件。目录不存在返回 null，调用方回落到
    /// <see cref="PrepareAsync"/>。
    /// </summary>
    Task<CliWorkspace?> ReuseAsync(
        string workDirectory, CliRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 按 sidecar 清单精确回滚本次写入的文件/目录。
    /// </summary>
    /// <param name="workspace">要清理的工作区。</param>
    /// <param name="removeAll">
    /// true = 连同隔离根目录一起删。<b>用户提供的工作目录永远不会被删</b>，
    /// 无论这个参数是什么。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CleanupAsync(CliWorkspace workspace, bool removeAll, CancellationToken cancellationToken);
}
