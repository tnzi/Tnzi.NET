
namespace Tnzi.Chat.Tests.Integration;

/// <summary>
/// Chat 模块集成测试基类
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<ChatTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// Chat 测试用 DbContext
/// </summary>
public class ChatTestDbContext : TnziDbContext<ChatTestDbContext>
{
    public ChatTestDbContext(
        DbContextOptions<ChatTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageReceive> MessageReceives => Set<MessageReceive>();
    public DbSet<MessageReply> MessageReplies => Set<MessageReply>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Chat 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageReceiveConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageReplyConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageRoleConfiguration());

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
