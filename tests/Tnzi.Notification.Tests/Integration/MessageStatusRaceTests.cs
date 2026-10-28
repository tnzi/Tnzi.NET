using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 取消、重试与发送在同一行上争用时，谁都不能把对方刚写下的状态盖掉：在真库上把并发排成确定的先后。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>批量取消</b>此前把整批消息连同收件人读进来、改完再整图写回。读与写之间被排队的工作项认领走、
/// 已经送达了几个人的消息，会被写成 <c>Cancelled</c>，刚记成 <c>Sent</c> 的收件人也被写回 <c>Cancelled</c>：
/// 人收到了，投递报告却说没有。单条取消早已改成条件更新，批量取消是同形的另一处。
/// </para>
/// <para>
/// ★★ <b>认领之后的那次保存</b>：发送路径认领到消息之后，又把被跟踪的那份改成 <c>Sending</c> 并立刻保存。
/// 那是一次无条件的写：认领与保存之间落下的取消被它盖回 <c>Sending</c>，循环照常把剩下的人发完。
/// </para>
/// <para>
/// ★★ <b>重试</b>同样是读改写：读与写之间「重发失败项」认领了消息并送达了一个人，整行写回把消息改回 <c>Pending</c>、
/// 把那个人写回 <c>Pending</c>，这次重试于是把他再发一遍。
/// </para>
/// <para>
/// 「刚刚提交的并发修改」由一个命令拦截器在目标语句之前（或之后）、在同一个连接与事务上插进去。
/// </para>
/// </remarks>
public class MessageStatusRaceTests : IntegrationTestBase
{
    private const int RecipientCount = 3;

    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly CommandHook _hook = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_hook);

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);
        AddRepo<OptOut>(services);
        AddRepo<Preference>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { MaxConcurrency = 4 });
        services.AddSingleton(_ => options.Object);

        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();
        // 没有队列服务：重试当场发送，重复投递直接体现在发送器的调用次数上。
        services.AddScoped<INotificationRetryService, NotificationRetryService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private INotificationService Service => ServiceProvider.GetRequiredService<INotificationService>();

    private string MessageTable => DbContext.Model.FindEntityType(typeof(Message))!.GetTableName()!;

    private string RecipientTable => DbContext.Model.FindEntityType(typeof(Recipient))!.GetTableName()!;

    // ── 批量取消 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 批量取消读出候选之后、写下之前，排队的工作项认领了这条消息并送达了第一个人：
    /// 那个人的 <c>Sent</c> 不被写回 <c>Cancelled</c>，已经开始发送的消息也不被算作取消。
    /// </summary>
    [Fact]
    public async Task BatchCancel_DoesNotOverwriteAMessageClaimedAndDeliveredInBetween()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);

        _hook.ArmBefore("UPDATE",
            $"UPDATE \"{MessageTable}\" SET \"Status\" = {(int)NotificationStatus.Sending}; " +
            $"UPDATE \"{RecipientTable}\" SET \"Status\" = {(int)NotificationStatus.Sent} WHERE \"Address\" = 'reader0@example.com';");

        var result = await Service.BatchCancelAsync([messageId]);

        _hook.Fired.ShouldBeTrue("the concurrent claim and delivery were never injected");
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(0, "a message that started sending before it could be cancelled is not reported as cancelled");

        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sending);
        var statuses = await RecipientStatusesAsync(messageId);
        statuses["reader0@example.com"].ShouldBe(NotificationStatus.Sent, "a recipient who received the message must not be recorded as cancelled");
    }

    /// <summary>批量取消只停 <c>Pending</c> / <c>Scheduled</c>、只停请求里的那些，收件人里已送达的不动。</summary>
    [Fact]
    public async Task BatchCancel_StopsOnlyUnsentRequestedMessages_AndLeavesDeliveredRecipients()
    {
        var pending = await SeedAsync(NotificationStatus.Pending);
        // 定时消息里已经有人送达（延后的那些还排着）：送达的那个保持 Sent。
        var scheduled = await SeedAsync(NotificationStatus.Scheduled, firstRecipient: NotificationStatus.Sent);
        var sent = await SeedAsync(NotificationStatus.Sent, allRecipients: NotificationStatus.Sent);
        var failed = await SeedAsync(NotificationStatus.Failed, allRecipients: NotificationStatus.Failed);
        var notRequested = await SeedAsync(NotificationStatus.Pending);

        var result = await Service.BatchCancelAsync([pending, scheduled, sent, failed]);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2);

        (await ReloadAsync(pending)).Status.ShouldBe(NotificationStatus.Cancelled);
        (await ReloadAsync(scheduled)).Status.ShouldBe(NotificationStatus.Cancelled);
        (await ReloadAsync(sent)).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadAsync(failed)).Status.ShouldBe(NotificationStatus.Failed);
        (await ReloadAsync(notRequested)).Status.ShouldBe(NotificationStatus.Pending);

        (await RecipientStatusesAsync(pending)).Values.ShouldAllBe(s => s == NotificationStatus.Cancelled);
        var scheduledRecipients = await RecipientStatusesAsync(scheduled);
        scheduledRecipients["reader0@example.com"].ShouldBe(NotificationStatus.Sent);
        scheduledRecipients.Where(r => r.Key != "reader0@example.com").Select(r => r.Value).ShouldAllBe(s => s == NotificationStatus.Cancelled);
        (await RecipientStatusesAsync(failed)).Values.ShouldAllBe(s => s == NotificationStatus.Failed);
        (await RecipientStatusesAsync(notRequested)).Values.ShouldAllBe(s => s == NotificationStatus.Pending);
    }

    // ── 重试 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 重试读出这条失败的消息之后、写回之前，「重发失败项」认领了它并送达了第一个人：
    /// 重试不把消息与那个人改回 <c>Pending</c>，也就不会把他再发一遍。
    /// </summary>
    [Fact]
    public async Task Retry_DoesNotRearmAMessageClaimedAndDeliveredInBetween()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, allRecipients: NotificationStatus.Failed);

        _hook.ArmBefore("UPDATE",
            $"UPDATE \"{MessageTable}\" SET \"Status\" = {(int)NotificationStatus.Sending}; " +
            $"UPDATE \"{RecipientTable}\" SET \"Status\" = {(int)NotificationStatus.Sent} WHERE \"Address\" = 'reader0@example.com';");

        var retry = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);

        _hook.Fired.ShouldBeTrue("the concurrent resend was never injected");
        retry.Succeeded.ShouldBeFalse("a message that left Failed before the retry was written is not retried");
        retry.Code.ShouldBe(409);
        VerifySent(Times.Never());

        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Sending);
        message.RetryCount.ShouldBe(0);
        (await RecipientStatusesAsync(messageId))["reader0@example.com"].ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对照：没有并发时重试照常重新武装并当场发完，只计一次重试。</summary>
    [Fact]
    public async Task Retry_WithoutARace_RearmsAndDeliversToTheFailedRecipients()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, allRecipients: NotificationStatus.Failed);

        var retry = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);

        retry.Succeeded.ShouldBeTrue(retry.Message);
        VerifySent(Times.Exactly(RecipientCount));
        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Sent);
        message.RetryCount.ShouldBe(1);
        (await RecipientStatusesAsync(messageId)).Values.ShouldAllBe(s => s == NotificationStatus.Sent);
    }

    // ── 认领之后 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 发送路径认领到消息之后、开始投递之前，管理员把它取消了：取消必须生效，一个人都不发。
    /// </summary>
    [Fact]
    public async Task ACancelLandingRightAfterTheSendClaim_IsNotOverwritten()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        _hook.ArmAfter("UPDATE", $"UPDATE \"{MessageTable}\" SET \"Status\" = {(int)NotificationStatus.Cancelled};");

        var send = await Service.SendAsync(messageId);

        _hook.Fired.ShouldBeTrue("the concurrent cancel was never injected");
        send.Succeeded.ShouldBeFalse("a send whose message was cancelled does not finish");
        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>同形的另一个入口：「重发失败项」认领之后落下的取消同样生效。</summary>
    [Fact]
    public async Task ACancelLandingRightAfterTheResendClaim_IsNotOverwritten()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, allRecipients: NotificationStatus.Failed);
        _hook.ArmAfter("UPDATE", $"UPDATE \"{MessageTable}\" SET \"Status\" = {(int)NotificationStatus.Cancelled};");

        var resend = await Service.ResendToFailedRecipientsAsync(messageId);

        _hook.Fired.ShouldBeTrue("the concurrent cancel was never injected");
        resend.Succeeded.ShouldBeFalse();
        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>对照：没有并发取消时，认领之后照常发完（否则上面验的可能是一个恒假的条件）。</summary>
    [Fact]
    public async Task WithoutACancel_TheClaimedSendDeliversToEveryone()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);

        var send = await Service.SendAsync(messageId);

        send.Succeeded.ShouldBeTrue(send.Message);
        VerifySent(Times.Exactly(RecipientCount));
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private async Task<Dictionary<string, NotificationStatus>> RecipientStatusesAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Recipient>().AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .ToDictionaryAsync(r => r.Address, r => r.Status);
    }

    private async Task<Guid> SeedAsync(
        NotificationStatus status,
        NotificationStatus allRecipients = NotificationStatus.Pending,
        NotificationStatus? firstRecipient = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = status,
            TotalRecipientCount = RecipientCount,
            Recipients = [.. Enumerable.Range(0, RecipientCount)
                .Select(i => new Recipient
                {
                    Address = $"reader{i}@example.com",
                    Status = i == 0 && firstRecipient is { } first ? first : allRecipients
                })],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    /// <summary>
    /// 在第一条以 <c>verb</c> 开头的语句之前（或执行之后），在同一个连接与事务上执行一段 SQL：模拟一次刚刚提交的并发修改。只触发一次。
    /// </summary>
    private sealed class CommandHook : DbCommandInterceptor
    {
        private string? _verb;
        private string? _sql;
        private bool _after;

        public bool Fired { get; private set; }

        public void ArmBefore(string verb, string sql) => Arm(verb, sql, after: false);

        public void ArmAfter(string verb, string sql) => Arm(verb, sql, after: true);

        private void Arm(string verb, string sql, bool after)
        {
            _verb = verb;
            _sql = sql;
            _after = after;
            Fired = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_after)
                await FireAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_after)
                await FireAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_after)
                await FireAsync(command, cancellationToken);
            return result;
        }

        private async Task FireAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (_sql == null || !command.CommandText.TrimStart().StartsWith(_verb!, StringComparison.OrdinalIgnoreCase))
                return;

            var sql = _sql;
            _sql = null;

            await using var injected = command.Connection!.CreateCommand();
            injected.Transaction = command.Transaction;
            injected.CommandText = sql;
            await injected.ExecuteNonQueryAsync(cancellationToken);
            Fired = true;
        }
    }
}
