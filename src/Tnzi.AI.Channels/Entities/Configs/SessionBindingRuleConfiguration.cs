namespace Tnzi.AI.Channels.Entities.Configs;

/// <summary>
/// SessionBindingRule 实体配置类
/// </summary>
public class SessionBindingRuleConfiguration : EntityTypeConfigurationBase<SessionBindingRule, Guid>
{
    public override void Configure(EntityTypeBuilder<SessionBindingRule> builder)
    {
        builder.Property(e => e.Channel).HasMaxLength(64);
        builder.Property(e => e.PeerKind).HasMaxLength(32);
        builder.Property(e => e.PeerId).HasMaxLength(256);
        builder.Property(e => e.Scope).HasConversion<string>().HasMaxLength(32);

        // 复合索引：按频道、Peer、优先级查询
        builder.HasIndex(e => new { e.Channel, e.PeerId, e.Priority })
            .HasDatabaseName("IX_SessionBindingRule_Channel_Peer_Priority");
    }
}
