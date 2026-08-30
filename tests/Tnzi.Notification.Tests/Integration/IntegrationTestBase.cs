
namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// Notification 模块集成测试基类
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<NotificationTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
        // 真实服务走 MapTo，而 MapperExtensions 持有的是**静态**映射器，正常由
        // MapsterModule 在应用启动时装上 —— 测试里没有那一步。
        // 在此之前这个类只是碰巧能过：同程序集的 Services/*Tests 在自己的构造函数里
        // SetMapper，只要它们先跑，静态字段就已经是好的。xUnit 的并行调度不保证这个
        // 顺序，本地核多先跑到 Services 于是全绿，CI 上顺序一变这里就 5 个全挂。
        // 同 Chat / Audit 两个模块的集成测试基类。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
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