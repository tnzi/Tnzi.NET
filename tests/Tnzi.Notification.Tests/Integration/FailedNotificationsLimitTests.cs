using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 管理端「失败通知」列表的条数有上界：每条消息连同全部收件人与附件一起加载。
/// </summary>
public class FailedNotificationsLimitTests : IntegrationTestBase
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

    private async Task SeedFailedAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            DbContext.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                Subject = $"s{i}",
                Content = "c",
                Type = NotificationType.Email,
                Status = NotificationStatus.Failed,
            });
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>★ 一个极大的 top 被截断在上限，不会把整张失败表连带子表一次读出。</summary>
    [Fact]
    public async Task AHugeTop_IsCappedAtTheMaximum()
    {
        await SeedFailedAsync(NotificationQueryService.MaxFailedTop + 10);

        var failed = await Query.GetFailedNotificationsAsync(top: int.MaxValue);

        failed.Succeeded.ShouldBeTrue(failed.Message);
        failed.Data!.Count().ShouldBe(NotificationQueryService.MaxFailedTop);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveTop_FallsBackToTheDefault(int top)
    {
        await SeedFailedAsync(NotificationQueryService.DefaultFailedTop + 5);

        var failed = await Query.GetFailedNotificationsAsync(top: top);

        failed.Data!.Count().ShouldBe(NotificationQueryService.DefaultFailedTop);
    }
}
