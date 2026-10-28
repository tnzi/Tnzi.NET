namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 能起外部 agent 子进程的地方。<see cref="ICliHostTarget"/> 在它之上再加文件系统、可执行文件查找与回写通道条目。
/// </summary>
/// <remarks>
/// 刻意<b>不</b>复用 <c>ISandbox.ExecuteCommandAsync</c>：那是「跑完一条命令、批量返回结果」的形状，
/// 没有流式，也没有 stdin 交互，装不下 ACP。
/// </remarks>
public interface ICliProcessHost
{
    /// <summary>
    /// 这个位置的名字，进日志与失败消息（「在 X 上找不到可执行文件」）。本机宿主是机器名。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 启动子进程并返回双向 transport。实现负责进程组隔离、信号与整树终止。
    /// </summary>
    /// <exception cref="CliExecutableNotFoundException">可执行文件不在。</exception>
    /// <exception cref="CliProcessLaunchException">起不来（权限、格式、被拒绝的参数）。</exception>
    Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken);
}

/// <summary>
/// 一个已启动的外部 agent 子进程。
/// </summary>
public interface ICliProcess : IAsyncDisposable
{
    /// <summary>双向 stdio 通道。</summary>
    ICliAgentTransport Transport { get; }

    /// <summary>进程 ID（诊断用；远程宿主给它在自己那里的 id）。</summary>
    int ProcessId { get; }

    /// <summary>进程是否已退出。</summary>
    bool HasExited { get; }

    /// <summary>退出码。进程未退出时为 null。</summary>
    int? ExitCode { get; }

    /// <summary>
    /// 重新附着句柄：服务端重启后凭它经 <see cref="ICliHostTarget.ReattachAsync"/> 接回这个进程。null = 接不回来（缺省，本机宿主）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 给出句柄就是承诺：服务端停机时只释放这个对象（<see cref="IAsyncDisposable.DisposeAsync"/>）、不调 <see cref="TerminateAsync"/>，
    /// 进程必须照常跑下去；宿主从进程启动起按顺序保留它写出的每一行 stdout，连同写这一行之前收到了几行 stdin
    /// （见 <see cref="ICliReattachedProcess"/>），直到运行被终止或者宿主自己的保留期限到了。
    /// 句柄的内容由宿主决定（容器 id + 进程号、代理侧的会话键），框架只把它原样存下来、原样交回去，长度上限 500。
    /// </para>
    /// <para>
    /// 服务端在起进程之后才把句柄写进运行记录：在那之前就崩溃的运行照旧重跑，宿主收到同一个运行 id 的新启动时应当先终止旧的那个进程。
    /// </para>
    /// </remarks>
    string? ReattachHandle => null;

    /// <summary>
    /// 整组优雅终止：先温和请求退出，等待宽限期，仍在则强制杀掉整棵进程树。
    /// </summary>
    /// <remarks>
    /// 只杀直接子进程是不够的：CLI 自己会拉起 MCP server 和工具子进程，它们会变成孤儿继续跑。
    /// 在一个没有硬超时的续接会话上，这些孤儿能持续烧掉数小时的模型预算。
    /// 实测中运行时的 claude 有 6 个后代进程。
    /// </remarks>
    Task TerminateAsync(CancellationToken cancellationToken);

    /// <summary>等待进程退出。</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}
