namespace Tnzi.AI.Sandbox.Models;

/// <summary>
/// 沙箱工具执行环境 - <c>SandboxMiddleware</c> 在 Agent 运行期间通过
/// <c>IAgentExecutionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment]</c>
/// 发布给 <see cref="Tools.SandboxTools"/> 的环境（AsyncLocal 通道，主管线与 AgentAsTools 子代理路径均可见）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>沙箱按需创建，不在中间件入口创建。</b>环境发布时只持有线程 Id 与一个工厂；
/// 第一次 <see cref="GetSandboxAsync"/> 才布置线程目录、复制技能资源、向 <see cref="ISandboxProvider"/> 要实例。
/// 一次运行里没有任何工具调用 <see cref="GetSandboxAsync"/>（Agent 根本没有沙箱工具、或这一轮模型没用它），
/// 磁盘上就什么都不发生，provider 也一次都不会被调用。此前每一次带线程的运行都先建目录、复制约 2 MB
/// 技能文件、再开一个沙箱，而多数运行与沙箱毫无关系；Production 下 Local provider 的守卫还会让这些运行
/// 一律失败。改成中间件入口按「Agent 有没有沙箱工具」判定行不通：AgentAsTools / Handoff / Router 的
/// 子 Agent 在 <c>next()</c> 内部才解析自己的工具，父 Agent 没有沙箱工具不等于子 Agent 没有。
/// </para>
/// <para>
/// 创建串行化：<c>ls</c> / <c>read_file</c> 标了 <c>IsConcurrencySafe</c>，执行器可能并行调用，
/// 两个并发的首次调用只能建出一个沙箱。创建失败<b>不缓存</b>：异常原样抛给调用方（执行器把它记成
/// 一次失败的工具调用），下一次调用重试；缓存失败会让一次 Docker 抖动毁掉整轮运行。
/// </para>
/// <para>
/// 环境拥有它创建（或经构造函数接管）的沙箱：<see cref="DisposeAsync"/> 释放它，之后再取抛
/// <see cref="ObjectDisposedException"/>。
/// </para>
/// </remarks>
public sealed class SandboxToolEnvironment : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ISandbox>>? _sandboxFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISandbox? _sandbox;
    private bool _disposed;

    /// <summary>
    /// 以一个已经存在的沙箱建环境（环境接管它的释放）。
    /// </summary>
    /// <param name="sandbox">活动沙箱实例。</param>
    /// <param name="threadId">沙箱绑定的线程 Id（虚拟路径翻译与线程配额使用同一 Id）。</param>
    public SandboxToolEnvironment(ISandbox sandbox, Guid threadId)
    {
        _sandbox = Check.NotNull(sandbox);
        ThreadId = threadId;
    }

    /// <summary>
    /// 以一个工厂建环境：沙箱在第一次 <see cref="GetSandboxAsync"/> 时才创建。
    /// </summary>
    /// <param name="threadId">沙箱绑定的线程 Id。</param>
    /// <param name="sandboxFactory">创建沙箱的工厂；只在首次成功前被调用，可能因失败重试而多次被调用。</param>
    public SandboxToolEnvironment(Guid threadId, Func<CancellationToken, Task<ISandbox>> sandboxFactory)
    {
        ThreadId = threadId;
        _sandboxFactory = Check.NotNull(sandboxFactory);
    }

    /// <summary>沙箱绑定的线程 Id。</summary>
    public Guid ThreadId { get; }

    /// <summary>沙箱是否已经创建出来（本次运行里至少有一个工具调用真的用到了它）。</summary>
    public bool IsSandboxCreated => _sandbox is not null;

    /// <summary>
    /// 取沙箱，没有就创建。并发调用只创建一个；创建失败把异常抛给调用方并留给下一次重试。
    /// </summary>
    /// <exception cref="ObjectDisposedException">环境已随运行结束释放。</exception>
    public async ValueTask<ISandbox> GetSandboxAsync(CancellationToken ct = default)
    {
        if (_sandbox is { } created) return created;
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct);
        try
        {
            if (_sandbox is { } raced) return raced;
            ObjectDisposedException.ThrowIf(_disposed, this);

            var sandbox = await _sandboxFactory!(ct);
            _sandbox = sandbox;
            return sandbox;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 释放已创建的沙箱；没创建过就什么都不做。等待进行中的创建结束，不让刚建出来的实例泄漏。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;

            var sandbox = _sandbox;
            _sandbox = null;
            if (sandbox is not null)
            {
                await sandbox.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
