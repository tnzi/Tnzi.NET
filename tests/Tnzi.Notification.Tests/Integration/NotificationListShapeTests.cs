using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 管理端列表与投递报告读多少行：在真库上验证。
/// </summary>
/// <remarks>
/// ★★ <b>补的是哪个洞。</b>消息列表与已排期列表把 <c>Message</c> 投影成 <c>NotificationInfo</c>，而 DTO 上挂着收件人与附件两个集合：
/// 每一行带出它的全部收件人，整份正文按收件人行数重复。几场两万人群发之后，打开一页 20 行的列表就是几十万行。
/// 列表行只用两个计数。投递报告则把一条消息的全部收件人读进内存再整份序列化，而抽屉只渲染前 200 行。
/// </remarks>
public class NotificationListShapeTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        services.AddScoped<INotificationQueryService, NotificationQueryService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private INotificationQueryService Query => ServiceProvider.GetRequiredService<INotificationQueryService>();

    // ── 列表行 ───────────────────────────────────────────────────────────────

    /// <summary>★★★ 消息列表的行不带收件人与附件。</summary>
    [Fact]
    public async Task AQueryPageRow_CarriesNoRecipientsOrAttachments()
    {
        await SeedAsync(NotificationStatus.Sent, recipients: 5);

        var page = await Query.QueryAsync(new QueryNotificationRequest());

        var row = page.Data!.Items.ShouldHaveSingleItem();
        row.Recipients.ShouldBeEmpty();
        row.Attachments.ShouldBeEmpty();
        row.TotalRecipientCount.ShouldBe(5);
    }

    /// <summary>★★ 已排期列表同一个形状。</summary>
    [Fact]
    public async Task AScheduledPageRow_CarriesNoRecipientsOrAttachments()
    {
        await SeedAsync(NotificationStatus.Scheduled, recipients: 5);

        var page = await Query.GetScheduledAsync(new QueryNotificationRequest());

        var row = page.Data!.Items.ShouldHaveSingleItem();
        row.Recipients.ShouldBeEmpty();
        row.Attachments.ShouldBeEmpty();
    }

    /// <summary>
    /// ★★ 列表行的每一个标量字段都与详情一致：列表投影是逐字段列出来的，新增字段漏补一行时这里会红，
    /// 而不是让列表上那一列安静地变成默认值（抽屉里的正文、失败原因就是从列表行读的）。
    /// </summary>
    [Fact]
    public async Task AListRow_CarriesEveryFieldTheDetailDoes()
    {
        var id = await SeedAsync(NotificationStatus.Scheduled, recipients: 2, failed: 1);

        var row = (await Query.QueryAsync(new QueryNotificationRequest())).Data!.Items.ShouldHaveSingleItem();
        var detail = (await Query.GetByIdAsync(id)).Data!;

        var scalars = typeof(NotificationInfo).GetProperties()
            .Where(p => p.Name is not nameof(NotificationInfo.Recipients) and not nameof(NotificationInfo.Attachments));
        foreach (var property in scalars)
        {
            var expected = property.GetValue(detail);
            expected.ShouldNotBe(DefaultOf(property.PropertyType), $"seed {property.Name} with a non-default value so this comparison means something");
            property.GetValue(row).ShouldBe(expected, $"list rows drop {property.Name}");
        }
    }

    // ── 投递报告 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 计数按全部收件人算，明细最多 <see cref="NotificationQueryService.MaxReportRecipients"/> 行：
    /// 失败的排最前，其次被拦下的，然后其余。
    /// </summary>
    [Fact]
    public async Task TheDeliveryReport_CountsEveryoneButListsAtMostTheCap_FailuresFirst()
    {
        var total = NotificationQueryService.MaxReportRecipients + 50;
        var id = await SeedAsync(NotificationStatus.PartiallySent, recipients: total, failed: 10, cancelled: 5);

        var report = (await Query.GetDeliveryReportAsync(id)).Data!;

        report.TotalRecipients.ShouldBe(total);
        report.FailedCount.ShouldBe(10);
        report.SentCount.ShouldBe(total - 15);
        report.Recipients.Count.ShouldBe(NotificationQueryService.MaxReportRecipients);
        report.Recipients.Take(10).ShouldAllBe(r => r.Status == NotificationStatus.Failed);
        report.Recipients.Skip(10).Take(5).ShouldAllBe(r => r.Status == NotificationStatus.Cancelled);
    }

    /// <summary>对照：装得下时整份名单都在，计数与明细一致。</summary>
    [Fact]
    public async Task ASmallDeliveryReport_ListsEveryRecipient()
    {
        var id = await SeedAsync(NotificationStatus.PartiallySent, recipients: 4, failed: 1);

        var report = (await Query.GetDeliveryReportAsync(id)).Data!;

        report.TotalRecipients.ShouldBe(4);
        report.Recipients.Count.ShouldBe(4);
        report.Recipients[0].Status.ShouldBe(NotificationStatus.Failed);
        report.PendingCount.ShouldBe(0);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private async Task<Guid> SeedAsync(NotificationStatus status, int recipients, int failed = 0, int cancelled = 0)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Type = NotificationType.Sms,
            Subject = "Quarterly newsletter",
            Content = "<p>Body</p>",
            IsHtml = true,
            Status = status,
            SentTime = DateTime.UtcNow.AddMinutes(-5),
            FailureReason = "SMTP 451",
            RetryCount = 1,
            MaxRetryCount = 5,
            TotalRecipientCount = recipients,
            SuccessCount = recipients - failed - cancelled,
            FailureCount = failed,
            Priority = NotificationPriority.High,
            SenderId = Guid.NewGuid(),
            Category = "Marketing",
            IsTransactional = true,
            TemplateName = "newsletter",
            ProviderKey = "bulk",
            ScheduledTime = DateTime.UtcNow.AddMinutes(-30),
            Recipients = [.. Enumerable.Range(0, recipients).Select(i => new Recipient
            {
                // 失败与被拦下的放在名单末尾：排序要靠查询做，而不是碰巧落在前面。
                Address = $"+1555000{i:D4}",
                Status = i >= recipients - failed ? NotificationStatus.Failed
                    : i >= recipients - failed - cancelled ? NotificationStatus.Cancelled
                    : NotificationStatus.Sent,
            })],
            Attachments = [new Attachment { FileName = "invoice.pdf", FilePath = "https://files.example.com/invoice.pdf" }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
