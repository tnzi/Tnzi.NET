
namespace Tnzi.EFCore.Outbox.Configs;

/// <summary>
/// OutboxMessage 实体配置
/// 仅在 Outbox 功能启用时（EFCore:Outbox:Enabled = true）才参与模型构建
/// </summary>
public class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage, Guid>
{
    /// <summary>
    /// 由 EFCoreModule 在服务注册阶段根据 OutboxOptions.Enabled 设置
    /// </summary>
    internal static bool OutboxEnabled { get; set; } = false;

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
