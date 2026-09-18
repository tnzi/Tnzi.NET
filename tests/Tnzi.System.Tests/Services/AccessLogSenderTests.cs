namespace Tnzi.System.Tests.Services;

/// <summary>
/// <see cref="AccessLogSender"/> 的有界队列满了之后发生什么：丢弃必须<b>可见</b>（计数 + 日志），且丢的是新来的不是排队的。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前是 <c>BoundedChannelFullMode.DropWrite</c> 且 <c>SendAsync</c> 零记账。2026-09-12 起 <c>AccessLogMiddleware</c>
/// 每个请求往这条队列投一行，它第一次承受真实负载 —— 而把队列塞满的恰恰是流量高峰，统计页与趋势图在峰值处
/// 凭空少一段，一张以计数为全部用途的表却看不出自己少了多少。
/// </para>
/// <para>
/// 与 <c>Tnzi.Audit</c> 的 <c>AuditSenderTests</c> 同形。纯内存，与数据库无关。
/// </para>
/// </remarks>
public class AccessLogSenderTests
{
    [Fact]
    public async Task QueueFull_DropsTheNewestAndCountsAndLogs()
    {
        var logger = new CapturingLogger();
        var sender = new AccessLogSender(Options(queueCapacity: 2), logger);

        await sender.SendAsync(Log("/api/first"));
        await sender.SendAsync(Log("/api/second"));
        await sender.SendAsync(Log("/api/third"));

        sender.DroppedCount.ShouldBe(1);

        // 留在队列里的是先到的两条：旧记录是已经发生、正等待落库的证据，不能被新来的挤掉。
        var queued = new List<string>();
        while (sender.Reader.TryRead(out var log)) queued.Add(log.Path);
        queued.ShouldBe(["/api/first", "/api/second"]);

        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldContain("dropped");
    }

    [Fact]
    public async Task Drops_AreLoggedOnTheFirstAndThenEveryInterval()
    {
        var logger = new CapturingLogger();
        var sender = new AccessLogSender(Options(queueCapacity: 1), logger);

        await sender.SendAsync(Log("/api/queued"));
        var drops = AccessLogSender.DropLogInterval * 2 + 1;
        for (var i = 0; i < drops; i++)
            await sender.SendAsync(Log($"/api/dropped-{i}"));

        sender.DroppedCount.ShouldBe(drops);
        // 第 1 次、第 Interval 次、第 2×Interval 次。
        logger.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SendAsync_NeverBlocksTheCallerWhenTheQueueIsFull()
    {
        var sender = new AccessLogSender(Options(queueCapacity: 1), new CapturingLogger());
        await sender.SendAsync(Log("/api/queued"));

        // Wait 模式下若走 WriteAsync 这里会永远挂起（没有读者）；TryWrite 必须立即返回。
        var send = sender.SendAsync(Log("/api/overflow"));
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.ShouldBe(send);
    }

    [Fact]
    public void QueueCapacity_DefaultsTo5000_AndMustBePositive()
    {
        new AccessLogOptions().QueueCapacity.ShouldBe(5000);

        var errors = new AccessLogOptionsValidator().Validate(null, new AccessLogOptions { QueueCapacity = 0 });
        errors.Failed.ShouldBeTrue();
        errors.FailureMessage.ShouldContain("QueueCapacity");
    }

    private static IOptionsMonitor<AccessLogOptions> Options(int queueCapacity)
        => new StaticOptionsMonitor(new AccessLogOptions { Enabled = true, QueueCapacity = queueCapacity });

    private static AccessLogDto Log(string path) => new()
    {
        Path = path,
        Method = "GET",
        StatusCode = 200,
        CreationTime = DateTime.UtcNow,
    };

    private sealed class StaticOptionsMonitor(AccessLogOptions value) : IOptionsMonitor<AccessLogOptions>
    {
        public AccessLogOptions CurrentValue => value;
        public AccessLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AccessLogOptions, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger<AccessLogSender>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
