namespace Tnzi.Identity.Tests;

/// <summary>
/// <see cref="LoginLogSender"/> 的有界队列满了之后发生什么：丢弃必须<b>可见</b>（计数 + 日志），且丢的是新来的不是排队的。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前是 <c>BoundedChannelFullMode.DropWrite</c> 且 <c>SendAsync</c> 零记账：队列满时 <c>WriteAsync</c> 立即成功，
/// 被丢掉的那条既不返回也不可观测。<c>LoginSecurityService</c> 拿这张表做「频繁尝试」「异常来源地址」这类安全判定，
/// 而把 2000 格队列塞满的恰恰是一次撞库 —— 溢出的尝试无声消失，判定在最需要它的时刻少算，事后也没人看得出记录不全。
/// </para>
/// <para>
/// 与 <c>Tnzi.Audit</c> 的 <c>AuditSenderTests</c> 同形。纯内存，与数据库无关。
/// </para>
/// </remarks>
public class LoginLogSenderTests
{
    [Fact]
    public async Task QueueFull_DropsTheNewestAndCountsAndLogs()
    {
        var logger = new CapturingLogger();
        var sender = new LoginLogSender(logger, capacity: 2);

        await sender.SendAsync(Log("first"));
        await sender.SendAsync(Log("second"));
        await sender.SendAsync(Log("third"));

        sender.DroppedCount.ShouldBe(1);

        // 留在队列里的是先到的两条：旧记录是已经发生、正等待落库的证据，不能被新来的挤掉。
        var queued = new List<string>();
        while (sender.Reader.TryRead(out var log)) queued.Add(log.UserName!);
        queued.ShouldBe(["first", "second"]);

        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Error);
        logger.Entries[0].Message.ShouldContain("dropped");
    }

    [Fact]
    public async Task Drops_AreLoggedOnTheFirstAndThenEveryInterval()
    {
        var logger = new CapturingLogger();
        var sender = new LoginLogSender(logger, capacity: 1);

        await sender.SendAsync(Log("queued"));
        var drops = LoginLogSender.DropLogInterval * 2 + 1;
        for (var i = 0; i < drops; i++)
            await sender.SendAsync(Log($"dropped-{i}"));

        sender.DroppedCount.ShouldBe(drops);
        // 第 1 次、第 Interval 次、第 2×Interval 次。
        logger.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SendAsync_NeverBlocksTheCallerWhenTheQueueIsFull()
    {
        var sender = new LoginLogSender(new CapturingLogger(), capacity: 1);
        await sender.SendAsync(Log("queued"));

        // Wait 模式下若走 WriteAsync 这里会永远挂起（没有读者）；TryWrite 必须立即返回。
        var send = sender.SendAsync(Log("overflow"));
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.ShouldBe(send);
    }

    [Fact]
    public void DefaultCapacity_IsTheDocumentedOne()
    {
        LoginLogSender.Capacity.ShouldBe(2000);
        Should.Throw<ArgumentOutOfRangeException>(() => new LoginLogSender(new CapturingLogger(), capacity: 0));
    }

    private static LoginLog Log(string userName) => new()
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        IpAddress = "203.0.113.7",
        Status = LoginStatus.Failed,
    };

    private sealed class CapturingLogger : ILogger<LoginLogSender>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
