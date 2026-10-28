using System.Threading.Channels;
using Tnzi.Services;

namespace Tnzi.System.Tests.Services;

/// <summary>
/// 访问日志落库不依赖 IP 定位：框架不再默认注册 <see cref="IIpLocatorService"/>（自带实现把访问者 IP 交给第三方），
/// 没有定位实现时整批照常落库、归属地各列留空。
/// </summary>
public class AccessLogBackgroundServiceIpLocatorTests
{
    private sealed class Consumer : IAccessLogConsumer
    {
        public ChannelReader<AccessLogDto> Reader { get; } = Channel.CreateUnbounded<AccessLogDto>().Reader;
    }

    private sealed class ExposedService(IServiceProvider serviceProvider)
        : AccessLogBackgroundService(new Consumer(), serviceProvider, Mock.Of<ILogger<AccessLogBackgroundService>>())
    {
        public Task RunAsync(IReadOnlyList<AccessLogDto> batch, IServiceProvider scoped)
            => ProcessBatchAsync(batch, scoped, CancellationToken.None);
    }

    private static (IServiceProvider Services, List<AccessLog> Stored) BuildServices(IIpLocatorService? locator)
    {
        var stored = new List<AccessLog>();
        var repository = new Mock<IRepository<AccessLog, Guid>>();
        repository
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<AccessLog>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AccessLog>, CancellationToken>((entities, _) => stored.AddRange(entities))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(repository.Object);
        if (locator != null)
        {
            services.AddSingleton(locator);
        }

        return (services.BuildServiceProvider(), stored);
    }

    private static IReadOnlyList<AccessLogDto> Batch() =>
    [
        new AccessLogDto { Id = Guid.NewGuid(), Path = "/api/a", Method = "GET", IpAddress = "203.0.113.7", StatusCode = 200 },
        new AccessLogDto { Id = Guid.NewGuid(), Path = "/api/b", Method = "POST", IpAddress = "203.0.113.8", StatusCode = 401 }
    ];

    [Fact]
    public async Task WithoutAnIpLocator_TheBatchIsStored_WithoutLocation()
    {
        using var _ = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));
        var (services, stored) = BuildServices(locator: null);

        await new ExposedService(services).RunAsync(Batch(), services);

        stored.Count.ShouldBe(2);
        stored.ShouldAllBe(log => log.IpCountry == null && log.IpFullAddress == null);
        stored.Select(log => log.Path).ShouldBe(["/api/a", "/api/b"]);
    }

    [Fact]
    public async Task WithAnExplicitlyRegisteredIpLocator_TheBatchIsEnriched()
    {
        using var _ = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));
        var locator = new Mock<IIpLocatorService>();
        locator
            .Setup(l => l.LocateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ip, CancellationToken _) => new IpLocationResult { IpAddress = ip, Country = "Canada" });
        var (services, stored) = BuildServices(locator.Object);

        await new ExposedService(services).RunAsync(Batch(), services);

        stored.Count.ShouldBe(2);
        stored.ShouldAllBe(log => log.IpCountry == "Canada");
    }
}
