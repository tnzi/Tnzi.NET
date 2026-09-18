
namespace Tnzi.EFCore.Outbox.Configs;

/// <summary>
/// OutboxMessage 实体配置
/// 仅在 Outbox 功能启用时（EFCore:Outbox:Enabled = true）才参与模型构建
/// </summary>
public class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage, Guid>
{
    /// <summary>
    /// Outbox 是否参与建模。运行期由 <c>EFCoreModule</c> 在服务注册阶段按 <c>EFCore:Outbox:Enabled</c> 写入；
    /// 设计期由 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 从 appsettings 写入。
    /// </summary>
    /// <remarks>
    /// ★ 公开而不是 internal：它是进程级的建模开关，而 <c>dotnet ef</c> 不跑模块生命周期。
    /// 不用框架工厂基类、自己实现 <c>IDesignTimeDbContextFactory</c> 的消费方必须在建上下文前自己设它
    /// （与 <c>TableNamePrefixConfiguration.DesignTimeModuleContainer</c> 同一惯例），否则开了 Outbox
    /// 的应用生成的迁移里永远没有 <c>OutboxMessage</c> 表，首条集成事件写入时才炸。
    /// </remarks>
    public static bool OutboxEnabled { get; set; } = false;

    /// <summary>
    /// 当 Outbox 未启用时，返回一个哨兵类型，使该实体不被注册到任何 DbContext
    /// </summary>
    public override Type? DbContextType => OutboxEnabled ? null : typeof(OutboxMessageConfiguration);

    public override void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.EventData)
            .IsRequired();

        builder.Property(e => e.IsProcessed)
            .HasDefaultFalse();

        builder.Property(e => e.FailureCount)
            .HasDefaultValue(0);

        builder.Property(e => e.LastError)
            .HasMaxLength(2000);

        // 用于轮询未处理事件的复合索引
        builder.HasIndex(e => new { e.IsProcessed, e.CreationTime });
    }
}
