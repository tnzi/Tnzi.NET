using System.Net;
using System.Net.Sockets;

namespace Tnzi.AI.Infrastructure.Network;

/// <summary>
/// 线程安全的端口分配器 - 通过 socket 绑定验证端口可用性
/// </summary>
public class PortAllocator : IPortAllocator
{
    private readonly int _startPort;
    private readonly int _maxRange;
    private readonly HashSet<int> _reservedPorts = [];
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private int _nextScanOffset;

    public PortAllocator(IOptions<PortAllocatorOptions> options, ILogger<PortAllocator> logger)
    {
        Check.NotNull(options);
        _startPort = options.Value.StartPort;
        _maxRange = options.Value.MaxRange;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public PortReservation Allocate()
    {
        lock (_lock)
        {
            for (var i = 0; i < _maxRange; i++)
            {
                var offset = (_nextScanOffset + i) % _maxRange;
                var port = _startPort + offset;

                if (_reservedPorts.Contains(port))
                    continue;

                if (!IsPortAvailable(port))
                    continue;

                _reservedPorts.Add(port);
                _nextScanOffset = (offset + 1) % _maxRange;

                _logger.LogDebug("Allocated port {Port}", port);
                return new PortReservation(port, Release);
            }
        }

        throw new InvalidOperationException(
            $"No available ports in range {_startPort}-{_startPort + _maxRange - 1}. " +
            $"All {_maxRange} ports are either reserved or in use.");
    }

    /// <summary>
    /// 检查端口是否可用（通过 socket 绑定 0.0.0.0 验证）
    /// </summary>
    public static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private void Release(int port)
    {
        lock (_lock)
        {
            _reservedPorts.Remove(port);
            _logger.LogDebug("Released port {Port}", port);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        lock (_lock)
        {
            _reservedPorts.Clear();
        }
    }
}
