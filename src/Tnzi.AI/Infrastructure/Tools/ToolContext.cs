namespace Tnzi.AI.Infrastructure.Tools;

/// <summary>
/// IToolContext 默认实现 — 包装 Scoped ServiceProvider 供工具运行时访问
/// </summary>
internal sealed class ToolContext : IToolContext, IDisposable
{
    private readonly IServiceProvider _requestServices;

    public ToolContext(IServiceProvider requestServices, CancellationToken cancellationToken)
    {
        _requestServices = Check.NotNull(requestServices);
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc />
    public IServiceProvider RequestServices => _requestServices;

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <inheritdoc />
    public T? GetService<T>() where T : class
    {
        return _requestServices.GetService<T>();
    }

    /// <inheritdoc />
    public T GetRequiredService<T>() where T : notnull
    {
        return _requestServices.GetRequiredService<T>();
    }

    /// <summary>
    /// 创建 ToolContext 并设置 ToolContextAccessor.Current，返回的 IDisposable 在 Dispose 时清除
    /// </summary>
    public static IDisposable Establish(IServiceProvider serviceProvider, CancellationToken ct)
    {
        var context = new ToolContext(serviceProvider, ct);
        ToolContextAccessor.Current = context;
        return context;
    }

    public void Dispose()
    {
        ToolContextAccessor.Current = null;
    }
}
