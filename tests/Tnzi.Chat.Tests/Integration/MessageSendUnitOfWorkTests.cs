namespace Tnzi.Chat.Tests.Integration;

/// <summary>
/// SendAsync 在事务模式（全局 UnitOfWork + ExecuteInUnitOfWorkAsync 嵌套）下的回归测试。
/// 复现生产 bug：POST /messages 私信 500 —— 延迟保存模式下
/// <c>UpdateAsync(msg)</c> 把尚未持久化的 Added 实体改写为 Modified，
/// 导致 Message 的 INSERT 变成 UPDATE（0 行），请求尾部全局 UoW flush 时
/// 子表（MessageRecipient/MessageReceive）外键违反。
/// </summary>
public class MessageSendUnitOfWorkTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 真实仓储（共享 SQLite DbContext，serviceProvider 注入使其感知 IUnitOfWorkManager 而延迟保存）
        services.AddScoped<IRepository<Message, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, Message, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<MessageReceive, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, MessageReceive, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<MessageRecipient, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, MessageRecipient, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<MessageReply, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, MessageReply, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<MessageRole, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, MessageRole, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));

        // ChatTestDbContext 不包含 Identity User，SenderName 查询用 mock 兜底
        var userRepoMock = new Mock<IRepository<User, Guid>>();
        userRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        services.AddScoped(_ => userRepoMock.Object);

        // 让 UnitOfWorkManager 能发现测试 DbContext（生产环境经由配置/EntityManager 发现）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(ChatTestDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IMessageService, MessageService>();
    }

    public MessageSendUnitOfWorkTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);
    }

    [Fact]
    public async Task SendAsync_Should_Persist_Private_Message_Under_Global_UnitOfWork()
    {
        var uowManager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var service = ServiceProvider.GetRequiredService<IMessageService>();

        var senderId = Guid.NewGuid();
        var recipient1 = Guid.NewGuid();
        var recipient2 = Guid.NewGuid();

        // 模拟全局 UnitOfWorkFilter：请求开始时启用事务
        uowManager.EnableTransaction();

        var result = await service.SendAsync(senderId, new CreateMessageDto
        {
            Title = "UoW Test",
            Content = "private message under transaction",
            MessageType = MessageType.Private,
            RecipientIds = new List<Guid> { recipient1, recipient2 }
        });

        result.Succeeded.ShouldBeTrue(result.Message);

        // 嵌套提交应 flush：响应 DTO 的审计字段已填充（修复前 SendAsync 内层 commit 是 no-op，CreationTime 为 default）
        result.Data!.CreationTime.ShouldNotBe(default);

        // 模拟请求尾部全局 UnitOfWorkFilter 提交（生产 500 发生在这里）
        await uowManager.CommitTransactionAsync();

        // 数据完整性：消息 + 收件人关系 + 接收记录全部落库且外键一致
        var message = await DbContext.Messages.SingleAsync(m => m.Title == "UoW Test");
        message.Id.ShouldNotBe(Guid.Empty);
        message.IsSent.ShouldBeTrue();
        message.SenderId.ShouldBe(senderId);

        var recipients = await DbContext.Set<MessageRecipient>().ToListAsync();
        recipients.Count.ShouldBe(2);
        recipients.ShouldAllBe(r => r.MessageId == message.Id);

        var receives = await DbContext.MessageReceives.ToListAsync();
        receives.Count.ShouldBe(2);
        receives.ShouldAllBe(r => r.MessageId == message.Id);
    }

    [Fact]
    public async Task SendAsync_Should_Persist_Private_Message_Without_Ambient_Transaction()
    {
        // 对照组：无全局 UoW（仅 SendAsync 自身的 ExecuteInUnitOfWorkAsync）
        var service = ServiceProvider.GetRequiredService<IMessageService>();

        var senderId = Guid.NewGuid();
        var result = await service.SendAsync(senderId, new CreateMessageDto
        {
            Title = "No Ambient UoW",
            Content = "private message",
            MessageType = MessageType.Private,
            RecipientIds = new List<Guid> { Guid.NewGuid() }
        });

        result.Succeeded.ShouldBeTrue(result.Message);

        var message = await DbContext.Messages.SingleAsync(m => m.Title == "No Ambient UoW");
        message.IsSent.ShouldBeTrue();

        var recipients = await DbContext.Set<MessageRecipient>().ToListAsync();
        recipients.Count.ShouldBe(1);
        recipients[0].MessageId.ShouldBe(message.Id);
    }
}
