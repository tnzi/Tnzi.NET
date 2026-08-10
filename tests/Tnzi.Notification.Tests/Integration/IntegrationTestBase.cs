
namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// Notification 模块集成测试基类
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<NotificationTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// Notification 测试用 DbContext
/// </summary>
public class NotificationTestDbContext : TnziDbContext<NotificationTestDbContext>
{
    public NotificationTestDbContext(
        DbContextOptions<NotificationTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<OptOut> OptOuts => Set<OptOut>();

    // Template 模块实体
    public DbSet<Tnzi.Template.Entities.Template> Templates => Set<Tnzi.Template.Entities.Template>();
    public DbSet<Layout> Layouts => Set<Layout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Notification 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.RecipientConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.AttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.OptOutConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.PreferenceConfiguration());

        // 应用 Template 模块实体配置
        modelBuilder.ApplyConfiguration(new Tnzi.Template.Entities.Configs.TemplateConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Template.Entities.Configs.LayoutConfiguration());

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}