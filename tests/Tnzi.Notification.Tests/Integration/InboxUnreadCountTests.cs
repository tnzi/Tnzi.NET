
namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 收件箱的未读总数与列表、分类明细是同一个集合：被软删除的消息三处都不算。
/// </summary>
/// <remarks>
/// 总数此前单独 <c>CountAsync</c>，只按收件人列过滤；聚合时 <c>Include</c> 被忽略，不连 <c>Message</c>，消息的软删除过滤器也就不生效。
/// 列表与分类明细经必选导航访问 <c>Message</c>，EF 连的是过滤后的消息集，被删的消息自然掉出去。
/// 管理员批量删掉一条误发的群发后，每个人的角标仍把它算在内，而列表里看不到它，角标永远消不掉。
/// </remarks>
public class InboxUnreadCountTests : IntegrationTestBase
{
    private static readonly Guid Reader = Guid.Parse("0e0de000-0000-0000-0000-000000000001");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Recipient, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, Recipient, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IUserNotificationService, UserNotificationService>();
    }

    private IUserNotificationService Inbox => ServiceProvider.GetRequiredService<IUserNotificationService>();

    private async Task SeedAsync(string category, bool deleted)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Type = NotificationType.Push,
            Subject = $"{category} notice",
            Content = "Body",
            Status = NotificationStatus.Sent,
            Category = category,
            IsDeleted = deleted,
            Recipients = [new Recipient { Address = Reader.ToString(), UserId = Reader, Status = NotificationStatus.Sent }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task TheUnreadTotal_LeavesOutSoftDeletedMessages_LikeTheListAndTheBreakdown()
    {
        await SeedAsync("Billing", deleted: false);
        await SeedAsync("Marketing", deleted: true);

        var counts = (await Inbox.GetUnreadCountAsync(Reader)).Data!;
        var list = (await Inbox.GetInboxAsync(Reader, new QueryUserNotificationRequest())).Data!;

        Assert.Single(list.Items);
        Assert.Equal(new Dictionary<string, int> { ["Billing"] = 1 }, counts.UnreadByCategory);
        Assert.Equal(1, counts.TotalUnread);
    }

    /// <summary>对照：没有被删的消息时总数就是各分类之和。</summary>
    [Fact]
    public async Task TheUnreadTotal_IsTheSumOfTheBreakdown()
    {
        await SeedAsync("Billing", deleted: false);
        await SeedAsync("Billing", deleted: false);
        await SeedAsync("Security", deleted: false);

        var counts = (await Inbox.GetUnreadCountAsync(Reader)).Data!;

        Assert.Equal(3, counts.TotalUnread);
        Assert.Equal(2, counts.UnreadByCategory["Billing"]);
        Assert.Equal(1, counts.UnreadByCategory["Security"]);
    }
}
