using System.Net;
using System.Net.Sockets;
using Tnzi.AI.Infrastructure.Network;

namespace Tnzi.AI.Tests.Network;

public class PortAllocatorTests : IDisposable
{
    private readonly PortAllocator _allocator;
    private readonly List<IDisposable> _disposables = [];

    public PortAllocatorTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PortAllocatorOptions
        {
            StartPort = 19000,
            MaxRange = 100
        });
        var logger = NullLoggerFactory.Instance.CreateLogger<PortAllocator>();
        _allocator = new PortAllocator(options, logger);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        _allocator.Dispose();
    }

    [Fact]
    public void Allocate_ReturnsPortInRange()
    {
        var reservation = _allocator.Allocate();
        _disposables.Add(reservation);

        reservation.Port.ShouldBeGreaterThanOrEqualTo(19000);
        reservation.Port.ShouldBeLessThan(19100);
    }

    [Fact]
    public void Allocate_TwoCalls_ReturnsDifferentPorts()
    {
        var r1 = _allocator.Allocate();
        var r2 = _allocator.Allocate();
        _disposables.Add(r1);
        _disposables.Add(r2);

        r1.Port.ShouldNotBe(r2.Port);
    }

    [Fact]
    public void Allocate_DisposedReservation_PortIsReusable()
    {
        var r1 = _allocator.Allocate();
        var port1 = r1.Port;
        r1.Dispose();

        var allocated = new List<PortReservation>();
        var found = false;
        for (var i = 0; i < 100; i++)
        {
            var r = _allocator.Allocate();
            allocated.Add(r);
            if (r.Port == port1)
            {
                found = true;
                break;
            }
        }
        _disposables.AddRange(allocated);

        found.ShouldBeTrue($"Port {port1} should be reused after disposal");
    }

    [Fact]
    public void Allocate_ConcurrentAccess_NoDuplicates()
    {
        var reservations = new ConcurrentBag<PortReservation>();
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 20, _ =>
        {
            try
            {
                var r = _allocator.Allocate();
                reservations.Add(r);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        _disposables.AddRange(reservations);

        exceptions.ShouldBeEmpty();
        var ports = reservations.Select(r => r.Port).ToList();
        ports.Count.ShouldBe(ports.Distinct().Count());
    }

    [Fact]
    public void Allocate_ExhaustedRange_ThrowsInvalidOperation()
    {
        var smallOptions = Microsoft.Extensions.Options.Options.Create(new PortAllocatorOptions
        {
            StartPort = 19200,
            MaxRange = 3
        });
        var logger = NullLoggerFactory.Instance.CreateLogger<PortAllocator>();
        using var smallAllocator = new PortAllocator(smallOptions, logger);

        var reservations = new List<PortReservation>();
        for (var i = 0; i < 3; i++)
        {
            try
            {
                reservations.Add(smallAllocator.Allocate());
            }
            catch (InvalidOperationException)
            {
                // 端口被系统占用也算正常
            }
        }
        _disposables.AddRange(reservations);

        Should.Throw<InvalidOperationException>(() => smallAllocator.Allocate());
    }

    [Fact]
    public void IsPortAvailable_OccupiedPort_ReturnsFalse()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var occupiedPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        PortAllocator.IsPortAvailable(occupiedPort).ShouldBeFalse();
        listener.Stop();
    }

    [Fact]
    public void IsPortAvailable_FreePort_ReturnsTrue()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        PortAllocator.IsPortAvailable(port).ShouldBeTrue();
    }
}
