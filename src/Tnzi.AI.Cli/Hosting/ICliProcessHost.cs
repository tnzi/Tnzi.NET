namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 外部 agent 子进程的宿主。
/// </summary>
/// <remarks>
/// 抽象出来是为了让 P4 的沙箱执行（Docker / K8s）接进同一条路径 —— 届时新增一个实现即可，
/// 适配器与调度层一行不改。刻意<b>不</b>复用 <c>ISandbox.ExecuteCommandAsync</c>：
/// 那是「跑完一条命令、批量返回结果」的形状，没有流式，也没有 stdin 交互，装不下 ACP。
/// </remarks>
public interface ICliProcessHost
{
    /// <summary>宿主名（用于日志与诊断）。</summary>
    string Name { get; }

    /// <summary>启动子进程并返回双向 transport。实现负责进程组隔离与整树终止。</summary>
    Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken);
}

/// <summary>
/// 一个已启动的外部 agent 子进程。
/// </summary>
public interface ICliProcess : IAsyncDisposable
{
    /// <summary>双向 stdio 通道。</summary>
    ICliAgentTransport Transport { get; }

    /// <summary>进程 ID（诊断用）。</summary>
    int ProcessId { get; }

    /// <summary>进程是否已退出。</summary>
    bool HasExited { get; }

    /// <summary>退出码。进程未退出时为 null。</summary>
    int? ExitCode { get; }

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
