namespace Tnzi.AI.Infrastructure.Network;

/// <summary>
/// 端口分配器接口 — 线程安全的端口分配与管理
/// </summary>
public interface IPortAllocator : IDisposable
{
    /// <summary>
    /// 分配一个可用端口（通过 socket 绑定验证可用性）
    /// </summary>
    /// <returns>端口预留（Dispose 时释放端口回池）</returns>
    /// <exception cref="InvalidOperationException">端口范围耗尽</exception>
    PortReservation Allocate();
}

/// <summary>
/// 端口预留 — Dispose 时自动释放端口回分配池
/// </summary>
public sealed class PortReservation : IDisposable
{
    private readonly Action<int>? _releaseAction;
    private int _disposed;

    public int Port { get; }

    public PortReservation(int port, Action<int>? releaseAction = null)
    {
        Port = port;
        _releaseAction = releaseAction;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            _releaseAction?.Invoke(Port);
        }
    }
}
