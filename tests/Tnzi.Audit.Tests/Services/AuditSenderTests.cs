using Microsoft.Extensions.Logging;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// <see cref="AuditSender"/> 的有界队列满了之后发生什么：丢弃必须<b>可见</b>（计数 + 日志），且丢的是新来的不是排队的。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前是 <c>BoundedChannelFullMode.DropOldest</c> 且 <c>SendAsync</c> 零记账：队列满时 <c>WriteAsync</c> 立即成功，
/// 被挤掉的那条既不返回也不可观测 —— 而挤掉的是<b>已经排队等着落库的旧记录</b>，恰好是这波高峰之前发生的、
/// 事后要回溯的那些。本模块自己的纪律是「审计数据真实缺失须可见」（条目截断记 Warning），通道溢出是同一类缺失。
/// </para>
/// <para>
/// 纯内存，与数据库无关。
/// </para>
/// </remarks>
public class AuditSenderTests
{
    [Fact]
    public async Task QueueFull_DropsTheNewestAndCountsAndLogs()
    {
        var logger = new CapturingLogger();
        var sender = new AuditSender(new StaticOptionsMonitor<AuditOptions>(new AuditOptions { ChannelCapacity = 2 }), logger);

        await sender.SendAsync(Operation("first"));
        await sender.SendAsync(Operation("second"));
        await sender.SendAsync(Operation("third"));

        sender.DroppedCount.ShouldBe(1);

        // 留在队列里的是先到的两条：旧记录是已经发生、正等待落库的证据，不能被新来的挤掉。
        var queued = new List<string>();
        while (sender.Reader.TryRead(out var op)) queued.Add(op.FunctionName!);
        queued.ShouldBe(["first", "second"]);

        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Error);
        logger.Entries[0].Message.ShouldContain("dropped");
    }

    [Fact]
    public async Task Drops_AreLoggedOnTheFirstAndThenEveryInterval()
    {
        var logger = new CapturingLogger();
        var sender = new AuditSender(new StaticOptionsMonitor<AuditOptions>(new AuditOptions { ChannelCapacity = 1 }), logger);

        await sender.SendAsync(Operation("queued"));
        var drops = AuditSender.DropLogInterval * 2 + 1;
        for (var i = 0; i < drops; i++)
            await sender.SendAsync(Operation($"dropped-{i}"));

        sender.DroppedCount.ShouldBe(drops);
        // 第 1 次、第 Interval 次、第 2×Interval 次。
        logger.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task QueueUnbounded_NeverDrops()
    {
        var logger = new CapturingLogger();
        var sender = new AuditSender(new StaticOptionsMonitor<AuditOptions>(new AuditOptions { ChannelCapacity = 0 }), logger);

        for (var i = 0; i < 50; i++)
            await sender.SendAsync(Operation($"op-{i}"));

        sender.DroppedCount.ShouldBe(0);
        var queued = 0;
        while (sender.Reader.TryRead(out _)) queued++;
        queued.ShouldBe(50);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_NeverBlocksTheCallerWhenTheQueueIsFull()
    {
        var sender = new AuditSender(new StaticOptionsMonitor<AuditOptions>(new AuditOptions { ChannelCapacity = 1 }), new CapturingLogger());
        await sender.SendAsync(Operation("queued"));

        // Wait 模式下若走 WriteAsync 这里会永远挂起（没有读者）；TryWrite 必须立即返回。
        var send = sender.SendAsync(Operation("overflow"));
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.ShouldBe(send);
    }

    private static AuditOperation Operation(string name) => new()
    {
        Id = Guid.NewGuid(),
        FunctionName = name,
        Url = "/api/x",
        HttpMethod = "GET",
        StartTime = DateTime.UtcNow,
        CreationTime = DateTime.UtcNow,
    };

    private sealed class CapturingLogger : ILogger<AuditSender>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
