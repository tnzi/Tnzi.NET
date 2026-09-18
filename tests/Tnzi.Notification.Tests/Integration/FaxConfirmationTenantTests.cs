using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.MultiTenancy;
using Tnzi.Notification.Events;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 多租户<b>开启</b>时，传真回执要能从一个<b>没有租户</b>的作用域对上某个租户的收件人。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞</b>：回执轮询（<c>FaxConfirmationBackgroundService</c>）每封信从根容器开一个
/// 新作用域，里面没有任何租户；<c>Recipient</c> 与 <c>Message</c> 都是多租户实体，全局过滤器塌成
/// <c>TenantId == null</c>，于是租户发出去的传真永远对不上号，回执被记成「没对上」，那条传真
/// 在库里、在投递报告里一直是 <c>Sent</c>，而回执邮件已被标已读、下一轮不会再来。
/// 与排队 / 恢复那条同根因，但回执<b>没有租户来源可以捕获</b>（它来自一个共享收件箱），
/// 所以修法是对号阶段跨租户找，找到后再切进那个租户写回。
/// </para>
/// <para>
/// ★ 夹具复用 <see cref="MultiTenantNotificationDbContext"/>（<c>MultiTenancy.Enabled=true</c>），
/// <c>ICurrentTenant</c> 是真实的 <see cref="CurrentTenant"/> 且按作用域注册 —— 过滤靠的是
/// DbContext 与切换租户的那个对象是同一个实例，mock 测不到这一层。SQLite 不掩盖任何断言：
/// 过滤器是 LINQ 表达式，在哪个库上都一样求值。
/// </para>
/// </remarks>
public class FaxConfirmationTenantTests : IntegratedTestBase<MultiTenantNotificationDbContext>
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly DateTime SentAt = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset ConfirmedAt = new(2026, 8, 20, 9, 5, 0, TimeSpan.Zero);

    private readonly List<NotificationFailedEvent> _failedEvents = [];

    public FaxConfirmationTenantTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ICurrentTenant>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        AddRepo<Message>(services);
        AddRepo<Recipient>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(MultiTenantNotificationDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<MultiTenantNotificationDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions { LookbackHours = 72 }
            }
        });
        services.AddSingleton(_ => options.Object);

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<NotificationFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationFailedEvent, CancellationToken>((e, _) => _failedEvents.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(_ => eventBus.Object);

        services.AddScoped<IFaxConfirmationService, FaxConfirmationService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<MultiTenantNotificationDbContext, TEntity, Guid>(
                sp.GetRequiredService<MultiTenantNotificationDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 用例 ─────────────────────────────────────────────────────────────────

    /// <summary>★★ 核心：租户 A 的传真，从无租户的作用域按 Message-ID 对上并记成失败。</summary>
    [Fact]
    public async Task TenantRecipient_FailureConfirmation_IsAppliedFromTenantlessScope()
    {
        var messageId = await SeedSentFaxAsync(TenantA);

        var result = await ApplyInFreshScopeAsync(Failure());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeTrue("the confirmation must match the tenant's recipient");

        var recipient = await ReadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Failed);
        recipient.TenantId.ShouldBe(TenantA);

        var message = await ReadMessageAsync(messageId);
        message.FailureCount.ShouldBe(1);
        message.SuccessCount.ShouldBe(0);
        message.Status.ShouldBe(NotificationStatus.Failed);
        message.TenantId.ShouldBe(TenantA);

        _failedEvents.Count.ShouldBe(1);
        _failedEvents[0].MessageId.ShouldBe(messageId);
    }

    /// <summary>按号码 + 时间窗那条路也要跨租户找。</summary>
    [Fact]
    public async Task ByNumberWithinLookback_MatchesAcrossTenant()
    {
        var messageId = await SeedSentFaxAsync(TenantA, carrierMessageId: null);

        var result = await ApplyInFreshScopeAsync(Failure(carrierMessageId: null, faxNumber: "9055551234"));

        result.Data.ShouldBeTrue(result.Message);
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Failed);
        (await ReadMessageAsync(messageId)).FailureCount.ShouldBe(1);
    }

    /// <summary>
    /// ★ 关掉全局过滤器时连软删过滤一起没了：已删除的消息不能被回执改动。
    /// </summary>
    [Fact]
    public async Task DeletedMessage_IsNotMatched()
    {
        var messageId = await SeedSentFaxAsync(TenantA, deleted: true);

        var result = await ApplyInFreshScopeAsync(Failure());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeFalse("a soft-deleted message is not a live delivery");
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
        _failedEvents.ShouldBeEmpty();
    }

    /// <summary>对照：没有租户的（host 级）传真照旧对得上 —— 跨租户对号不能把 null 那一格漏掉。</summary>
    [Fact]
    public async Task HostRecipient_StillMatched()
    {
        var messageId = await SeedSentFaxAsync(tenantId: null);

        var result = await ApplyInFreshScopeAsync(Failure());

        result.Data.ShouldBeTrue(result.Message);
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Failed);
        (await ReadMessageAsync(messageId)).FailureCount.ShouldBe(1);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    /// <summary>与 <c>FaxConfirmationBackgroundService.PollAsync</c> 同形：根容器开新作用域、无租户。</summary>
    private async Task<Result<bool>> ApplyInFreshScopeAsync(FaxConfirmation confirmation)
    {
        using var scope = ServiceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Id.ShouldBeNull("the polling scope carries no tenant");
        return await scope.ServiceProvider.GetRequiredService<IFaxConfirmationService>().ApplyAsync(confirmation);
    }

    private static FaxConfirmation Failure(
        string? carrierMessageId = "carrier-1@mail.example.com",
        string? faxNumber = null)
        => new(FaxDeliveryOutcome.Failed, carrierMessageId, faxNumber, "no answer", ConfirmedAt);

    private async Task<Guid> SeedSentFaxAsync(
        Guid? tenantId,
        string? carrierMessageId = "carrier-1@mail.example.com",
        bool deleted = false)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Subject = "File 2026-118",
            Content = "Archive only",
            Type = NotificationType.Fax,
            Status = NotificationStatus.Sent,
            SentTime = SentAt,
            TotalRecipientCount = 1,
            SuccessCount = 1,
            FailureCount = 0,
            IsDeleted = deleted,
            Recipients =
            [
                new Recipient
                {
                    TenantId = tenantId,
                    Address = "+1 (905) 555-1234",
                    Status = NotificationStatus.Sent,
                    SentTime = SentAt,
                    ExternalMessageId = carrierMessageId
                }
            ]
        };

        await DbContext.Set<Message>().AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private async Task<Recipient> ReadRecipientAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Recipient>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.MessageId == messageId);
    }

    private async Task<Message> ReadMessageAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == messageId);
    }
}
