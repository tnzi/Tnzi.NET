using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="FaxConfirmationBackgroundService"/> 的那一轮循环：一封坏信到底会不会拖累其余。
/// </summary>
/// <remarks>
/// ★★ <b>这条不是洁癖</b>：取信那一步已经把这一批标成已读了。所以一封信让循环中断，
/// 等于把剩下的回执**永久丢掉** —— 它们不会再出现在下一轮的未读里，
/// 而症状只是"有几份传真的失败一直没记上"，没有人会把它和某一封格式古怪的邮件联系起来。
/// </remarks>
public class FaxConfirmationPollTests
{
    private static FaxConfirmationMessage Mail(string number) => new()
    {
        Subject = $"Fax to {number} failed",
        ReceivedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)
    };

    /// <summary>★ 第二封炸了，第三封照样要被处理。</summary>
    [Fact]
    public async Task OneFailingMessage_DoesNotStopTheRest()
    {
        var seen = new List<string?>();

        var service = new Mock<IFaxConfirmationService>();
        service
            .Setup(s => s.ApplyAsync(It.IsAny<FaxConfirmation>(), It.IsAny<CancellationToken>()))
            .Returns<FaxConfirmation, CancellationToken>((confirmation, _) =>
            {
                seen.Add(confirmation.FaxNumber);
                return confirmation.FaxNumber == "9055550002"
                    ? throw new InvalidOperationException("boom")
                    : Task.FromResult(Result.Success(true));
            });

        var poller = BuildPoller(
            [Mail("9055550001"), Mail("9055550002"), Mail("9055550003")],
            service.Object);

        await poller.PollAsync(new FaxConfirmationOptions(), CancellationToken.None);

        seen.ShouldBe(["9055550001", "9055550002", "9055550003"]);
    }

    /// <summary>判读不出来的邮件不引起任何动作，也不打断后面的。</summary>
    [Fact]
    public async Task AnUnrecognisableMessage_IsSkippedQuietly()
    {
        var service = new Mock<IFaxConfirmationService>();
        service
            .Setup(s => s.ApplyAsync(It.IsAny<FaxConfirmation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var poller = BuildPoller(
            [new FaxConfirmationMessage { Subject = "Out of office" }, Mail("9055550003")],
            service.Object);

        await poller.PollAsync(new FaxConfirmationOptions(), CancellationToken.None);

        service.Verify(
            s => s.ApplyAsync(It.IsAny<FaxConfirmation>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>收件箱空的时候不该去碰落库那一层。</summary>
    [Fact]
    public async Task AnEmptyMailbox_TouchesNothing()
    {
        var service = new Mock<IFaxConfirmationService>();

        var poller = BuildPoller([], service.Object);

        await poller.PollAsync(new FaxConfirmationOptions(), CancellationToken.None);

        service.VerifyNoOtherCalls();
    }

    /// <summary>
    /// ★ 取信本身失败时让异常冒出去 —— 由 <c>ExecuteAsync</c> 记一条日志、下一轮再来。
    /// 这里吞掉它只会让"IMAP 密码过期"这种事完全没有声音。
    /// </summary>
    [Fact]
    public async Task AFailingMailbox_Propagates()
    {
        var mailbox = new Mock<IFaxConfirmationMailbox>();
        mailbox
            .Setup(m => m.FetchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("imap down"));

        var poller = BuildPoller(mailbox.Object, new Mock<IFaxConfirmationService>().Object);

        await Should.ThrowAsync<InvalidOperationException>(
            () => poller.PollAsync(new FaxConfirmationOptions(), CancellationToken.None));
    }

    private static FaxConfirmationBackgroundService BuildPoller(
        IReadOnlyList<FaxConfirmationMessage> messages, IFaxConfirmationService service)
    {
        var mailbox = new Mock<IFaxConfirmationMailbox>();
        mailbox
            .Setup(m => m.FetchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

        return BuildPoller(mailbox.Object, service);
    }

    private static FaxConfirmationBackgroundService BuildPoller(
        IFaxConfirmationMailbox mailbox, IFaxConfirmationService service)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => mailbox);
        services.AddScoped(_ => service);
        services.AddScoped<IFaxConfirmationParser, HeuristicFaxConfirmationParser>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());

        return new FaxConfirmationBackgroundService(
            services.BuildServiceProvider(),
            options.Object,
            NullLogger<FaxConfirmationBackgroundService>.Instance);
    }
}
