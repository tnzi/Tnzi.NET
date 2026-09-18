namespace Tnzi.System.Tests.Services;

/// <summary>
/// <c>GET admin/access-logs/statistics</c> 要如实报出「这个部署有没有在采集」：
/// 采集默认关闭，一个恒为 0 的总数与「没人访问」在界面上长得一模一样，只有这一位能区分。
/// </summary>
public class AccessLogStatisticsCaptureFlagTests
{
    private sealed class StaticOptionsMonitor(AccessLogOptions value) : IOptionsMonitor<AccessLogOptions>
    {
        public AccessLogOptions CurrentValue => value;
        public AccessLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AccessLogOptions, string?> listener) => null;
    }

    private static AccessLogService CreateService(List<AccessLog> logs, IOptionsMonitor<AccessLogOptions>? options)
    {
        var repository = new Mock<IRepository<AccessLog, Guid>>();
        var queryable = logs.BuildMock();
        repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(queryable);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new AccessLogService(serviceProvider.Object, repository.Object, new Mock<IAccessLogSender>().Object, options);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyTable_ReportsTheCaptureSwitch(bool enabled)
    {
        var service = CreateService([], new StaticOptionsMonitor(new AccessLogOptions { Enabled = enabled }));

        var result = await service.GetAccessLogStatisticsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalRequests.ShouldBe(0);
        result.Data.CaptureEnabled.ShouldBe(enabled);
    }

    [Fact]
    public async Task WithRows_ReportsTheCaptureSwitchAlongsideTheNumbers()
    {
        var logs = new List<AccessLog>
        {
            new() { Id = Guid.NewGuid(), Path = "/api/a", Method = "GET", StatusCode = 200, ResponseTime = 10, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Path = "/api/b", Method = "GET", StatusCode = 500, ResponseTime = 30, CreationTime = DateTime.UtcNow },
        };
        var service = CreateService(logs, new StaticOptionsMonitor(new AccessLogOptions { Enabled = true }));

        var result = await service.GetAccessLogStatisticsAsync();

        result.Data!.TotalRequests.ShouldBe(2);
        result.Data.ErrorRequests.ShouldBe(1);
        result.Data.CaptureEnabled.ShouldBeTrue();
    }

    /// <summary>拿不到选项（旧的手工构造）按未采集报，不抛。</summary>
    [Fact]
    public async Task NoOptions_ReportsNotCapturing()
    {
        var service = CreateService([], options: null);

        (await service.GetAccessLogStatisticsAsync()).Data!.CaptureEnabled.ShouldBeFalse();
    }
}
