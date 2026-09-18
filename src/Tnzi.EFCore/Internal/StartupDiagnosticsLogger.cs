namespace Tnzi.EFCore.Internal;

/// <summary>
/// 启动期（<c>ConfigureServicesAsync</c>）的诊断缓冲：那一阶段拿不到 DI 的 <see cref="ILogger"/>，
/// 先把日志调用原样收下，等 <c>OnApplicationInitializationAsync</c> 拿到 <see cref="ILoggerFactory"/> 后按原类别回放。
/// </summary>
/// <remarks>
/// 此前 DbContext 发现与注册的两处调用点都传 <c>logger: null</c>，2026-07-07 专为「重试策略 × UoW 手动事务互斥」
/// 加的告警、<c>[PRIMARY]</c> 信息与自动发现结果从落地那天起就没有打印过。缓冲保存的是完整的结构化日志调用
/// （状态、事件 Id、异常与格式化器），回放时目标 logger 的级别过滤照常生效。
/// 只回放一次；回放后再收到的调用直接丢弃（启动期之后不该再有人往这里写）。
/// </remarks>
internal sealed class StartupDiagnosticsLogger : ILogger
{
    private readonly object _gate = new();
    private readonly List<Action<ILogger>> _entries = [];
    private bool _replayed;

    public StartupDiagnosticsLogger(string category)
    {
        Category = Check.NotNullOrWhiteSpace(category);
    }

    /// <summary>回放时使用的日志类别（发出诊断的那个组件）。</summary>
    public string Category { get; }

    /// <summary>已缓冲、尚未回放的条数。</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Check.NotNull(formatter);

        lock (_gate)
        {
            if (_replayed)
            {
                return;
            }

            _entries.Add(target => target.Log(logLevel, eventId, state, exception, formatter));
        }
    }

    /// <summary>
    /// 把缓冲的诊断按 <see cref="Category"/> 回放到 <paramref name="loggerFactory"/> 创建的 logger 上，随后清空缓冲。
    /// </summary>
    public void ReplayTo(ILoggerFactory loggerFactory)
    {
        Check.NotNull(loggerFactory);

        List<Action<ILogger>> pending;
        lock (_gate)
        {
            if (_replayed)
            {
                return;
            }

            _replayed = true;
            pending = [.. _entries];
            _entries.Clear();
        }

        var target = loggerFactory.CreateLogger(Category);
        foreach (var entry in pending)
        {
            entry(target);
        }
    }
}
