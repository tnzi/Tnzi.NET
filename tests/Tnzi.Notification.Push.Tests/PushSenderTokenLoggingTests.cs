using Tnzi.TestBase;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 投递路径不得把设备令牌原文写进日志。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 令牌是凭据：注册表按令牌寻址，持有它的人可以改写或掐掉那台设备的推送归属。
/// 而每一次投递（成功、失败、令牌判死、APNs 未实现）都曾把它原样写进日志 ——
/// 日志是这套东西里传播最广的那份副本：它进聚合平台、进工单附件、进截图。
/// 这与本模块把注销从查询串改成 POST 请求体（避免令牌进访问日志）的理由直接矛盾。
/// </para>
/// <para>
/// ★ <b>掩码的纯函数测试守不住这件事</b>：<see cref="PushTokenMaskTests"/> 证明的是
/// 「这个函数会遮住尾部以外的部分」，删掉每一处调用它照样全绿。所以这里守的是<b>调用点</b>。
/// </para>
/// </remarks>
public class PushSenderTokenLoggingTests
{
    private const string Token = "cX9vQm2Ls0aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789abcdefTAILPART";

    /// <summary>
    /// 一条真实的投递调用（APNs 尚未实现那条分支，不碰任何外部服务）不得记下令牌原文。
    /// </summary>
    [Fact]
    public async Task A_delivery_attempt_does_not_write_the_raw_token_into_the_log()
    {
        var logger = new CapturingLogger<PushSender>();
        var options = new PushSenderOptions { Provider = "apns" };

        await new PushSender(options, logger).SendToAsync(Token, "Title", "Body");

        logger.Messages.ShouldNotBeEmpty("the delivery attempt logged nothing - this test would pass vacuously");
        logger.Messages.ShouldAllBe(m => !m.Contains(Token));
        logger.Messages.ShouldContain(m => m.Contains(PushTokenMask.Of(Token)));
    }

    /// <summary>
    /// 带点击目标与数据的发法同样只记掩码：被内容校验拒掉那一条、走到 provider 分派那一条都是。
    /// </summary>
    [Fact]
    public async Task A_delivery_attempt_with_link_and_data_does_not_write_the_raw_token_into_the_log()
    {
        var logger = new CapturingLogger<PushSender>();
        var sender = new PushSender(new PushSenderOptions { Provider = "apns" }, logger);

        await sender.SendToAsync(Token, new PushContent("Title", "Body") { Link = "/relative" });
        await sender.SendToAsync(Token, new PushContent("Title", "Body")
        {
            Link = "https://app.example.com/chat/42",
            Data = new Dictionary<string, string> { ["threadId"] = "42" },
        });

        logger.Messages.Count.ShouldBeGreaterThanOrEqualTo(2, "both attempts must have logged - otherwise this passes vacuously");
        logger.Messages.ShouldAllBe(m => !m.Contains(Token));
        logger.Messages.Count(m => m.Contains(PushTokenMask.Of(Token))).ShouldBeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// 源码级门禁：每一处把令牌交给日志的地方都要过掩码（<c>PushSender</c> 与父模块的两个回退）。
    /// </summary>
    /// <remarks>
    /// 上面那条只覆盖得到一条分支（其余几条要么需要真的连上 FCM，要么需要 FCM 抛出
    /// 特定错误码）。而这类缺陷的形态正是「五处里漏掉一处」—— 漏掉的那一处不会让
    /// 任何行为测试变红。所以按源码对账：<c>{DeviceToken}</c> 这个日志占位符出现的每一行，
    /// 它的实参必须是 <c>PushTokenMask.Of(...)</c>。
    /// <para>
    /// 两个回退（<c>NullPushSender</c> / <c>UnconfiguredPushSender</c>）也在扫描范围里：开发期宿主与
    /// 少装了包的部署同样每次投递都写一行日志，而那一行曾经原样带着令牌。
    /// </para>
    /// <para>
    /// 唯一豁免是主题地址那一条：<c>FcmTopicName.LooksLikeTopicAddress</c> 已经确定它以
    /// <c>/topics/</c> 开头 —— 那不是令牌，而调用方要看到自己填错的那个值。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Tnzi.Notification.Push", "PushSender.cs")]
    [InlineData("Tnzi.Notification", "NullPushSender.cs")]
    [InlineData("Tnzi.Notification", "UnconfiguredPushSender.cs")]
    public void Every_log_call_that_names_a_device_token_masks_it(string project, string file)
    {
        var path = Path.Combine(RepoRoot.Locate(), "src", project, "Services", file);
        File.Exists(path).ShouldBeTrue($"{path} not found - the scan would pass vacuously");

        var lines = File.ReadAllLines(path);
        var offenders = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("{DeviceToken}"))
                continue;

            // 实参可能落在下一行（日志模板与参数常常换行写）。
            var statement = lines[i] + (i + 1 < lines.Length ? lines[i + 1] : string.Empty);

            if (statement.Contains("is a topic address"))
                continue;

            if (!statement.Contains("PushTokenMask.Of("))
                offenders.Add($"line {i + 1}: {lines[i].Trim()}");
        }

        offenders.ShouldBeEmpty(
            "these log calls hand out the raw device token:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>对照：扫描真的看得见那些行（否则上一条在一个空集合上恒真）。</summary>
    [Theory]
    [InlineData("Tnzi.Notification.Push", "PushSender.cs", 5)]
    [InlineData("Tnzi.Notification", "NullPushSender.cs", 2)]
    [InlineData("Tnzi.Notification", "UnconfiguredPushSender.cs", 1)]
    public void The_scan_actually_finds_the_log_calls_it_is_guarding(string project, string file, int atLeast)
    {
        var path = Path.Combine(RepoRoot.Locate(), "src", project, "Services", file);

        File.ReadAllLines(path).Count(l => l.Contains("{DeviceToken}")).ShouldBeGreaterThanOrEqualTo(atLeast);
    }

    /// <summary>只记下格式化后消息文本的日志替身。</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
