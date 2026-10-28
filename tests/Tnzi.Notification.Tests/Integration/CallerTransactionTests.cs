using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 调用方在自己的工作单元事务里创建通知：通知与调用方先前的写入随调用方一起提交或回滚。
/// </summary>
/// <remarks>
/// 通知服务经注入的作用域 <c>IUnitOfWork</c> 保存。那个实例与工作单元管理器为同一上下文建的不是同一个，
/// 若它不加入管理器的事务，这次保存会在延迟事务 BEGIN 之前以自动提交落库，把调用方缓冲的写入一并提交；
/// 调用方随后回滚时已无物理事务可回滚。
/// </remarks>
public class CallerTransactionTests : IntegrationTestBase
{
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
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());
        services.AddSingleton(_ => options.Object);

        services.AddSingleton(_ => new Mock<IEmailSender>().Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();
    }

    [Fact]
    public async Task CreateAsync_InsideCallerTransactionThatRollsBack_LeavesNothingBehind()
    {
        var manager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var callerWrite = new OptOut { Address = "caller@example.com", Channel = NotificationType.Email };

        manager.EnableTransaction();
        await ServiceProvider.GetRequiredService<IRepository<OptOut, Guid>>().InsertAsync(callerWrite);

        var created = await ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(Request());
        created.Succeeded.ShouldBeTrue(created.Message);

        // 调用方后段失败
        await manager.RollbackTransactionAsync();

        DbContext.ChangeTracker.Clear();
        (await DbContext.Messages.AsNoTracking().CountAsync()).ShouldBe(0);
        (await DbContext.OptOuts.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CreateAsync_InsideCallerTransactionThatCommits_PersistsBoth()
    {
        var manager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var callerWrite = new OptOut { Address = "caller@example.com", Channel = NotificationType.Email };

        manager.EnableTransaction();
        await ServiceProvider.GetRequiredService<IRepository<OptOut, Guid>>().InsertAsync(callerWrite);

        var created = await ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(Request());
        created.Succeeded.ShouldBeTrue(created.Message);

        await manager.CommitTransactionAsync();

        DbContext.ChangeTracker.Clear();
        (await DbContext.Messages.AsNoTracking().CountAsync()).ShouldBe(1);
        (await DbContext.OptOuts.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    private static CreateNotificationRequest Request() => new()
    {
        Type = NotificationType.Email,
        Subject = "Subject",
        Content = "Body",
        Category = "General",
        Recipients = [new RecipientInput { Address = "someone@example.com" }],
    };

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }
}
