
namespace Tnzi.EFCore.Outbox.Configs;

/// <summary>
/// OutboxMessage 实体配置
/// </summary>
public class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage, Guid>
{
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
