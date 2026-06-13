namespace Tnzi.AI.Channels.Entities.Configs;

/// <summary>
/// ChannelThreadMapping 实体配置类
/// </summary>
public class ChannelThreadMappingConfiguration : EntityTypeConfigurationBase<ChannelThreadMapping, Guid>
{
    public override void Configure(EntityTypeBuilder<ChannelThreadMapping> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.ChannelName).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ChatId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.TopicId).HasMaxLength(128);
        builder.Property(e => e.ChannelUserId).HasMaxLength(128);

        // 唯一索引：channel + chatId + topicId（多租户开启时加 TenantId 前缀分区，
        // 与 SessionBindingRuleConfiguration 的条件索引模式一致；关闭时 TenantId 列被
        // DbContext Ignore 物理不存在，维持原索引不变）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.ChannelName, e.ChatId, e.TopicId })
                .IsUnique()
                .HasDatabaseName("IX_ChannelThreadMapping_Tenant_Channel_Chat_Topic");
        }
        else
        {
            builder.HasIndex(e => new { e.ChannelName, e.ChatId, e.TopicId })
                .IsUnique()
                .HasDatabaseName("IX_ChannelThreadMapping_Channel_Chat_Topic");
        }

        // 快速按 ThreadId 查找
        builder.HasIndex(e => e.ThreadId)
            .HasDatabaseName("IX_ChannelThreadMapping_ThreadId");
    }
}
