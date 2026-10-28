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

        // 唯一键：同一 (channel, chatId, topicId) 只映射到一条线程。服务层的判重是 read-then-write，
        // 而 ChannelManager 并发分派入站消息，同一会话两条几乎同时到达的首条消息都会读到「还没有」，
        // 所以最终把关的必须是数据库，DatabaseChannelThreadStore 的冲突重试分支也只在它把关时才走得到。
        // ★ 按 TopicId 有无拆成两条：绝大多数映射的 TopicId 是 NULL（只有 Telegram 论坛话题带它）。
        //   PostgreSQL / SQLite / MySQL 认为唯一索引里的 NULL 互不相等，SQL Server 上 EF 又会给可空列的
        //   唯一索引自动加 IS NOT NULL 过滤，单一条含 TopicId 的唯一索引在任何库上都挡不住两行
        //   (slack, D123, NULL)，于是同一会话的历史随读到哪一行而在两条线程间来回切换。
        // ★ 多租户时按租户分区，且 TenantId 为 NULL 的行（未归属租户的渠道写下的宿主行）同理再拆一对。
        //   关闭多租户时 TenantId 列被 DbContext Ignore，物理不存在。
        var topicSet = IndexFilterFactory.GetColumnNotNull("TopicId");
        var noTopic = IndexFilterFactory.GetColumnNull("TopicId");

        if (multiTenancyEnabled)
        {
            var tenantSet = IndexFilterFactory.GetColumnNotNull("TenantId");
            var hostOnly = IndexFilterFactory.GetColumnNull("TenantId");

            builder.HasIndex(e => new { e.TenantId, e.ChannelName, e.ChatId, e.TopicId })
                .IsUnique()
                .HasFilter($"{tenantSet} AND {topicSet}")
                .HasDatabaseName("IX_ChannelThreadMapping_Tenant_Channel_Chat_Topic");
            builder.HasIndex(e => new { e.TenantId, e.ChannelName, e.ChatId })
                .IsUnique()
                .HasFilter($"{tenantSet} AND {noTopic}")
                .HasDatabaseName("IX_ChannelThreadMapping_Tenant_Channel_Chat_NoTopic");
            builder.HasIndex(e => new { e.ChannelName, e.ChatId, e.TopicId })
                .IsUnique()
                .HasFilter($"{hostOnly} AND {topicSet}")
                .HasDatabaseName("IX_ChannelThreadMapping_Host_Channel_Chat_Topic");
            builder.HasIndex(e => new { e.ChannelName, e.ChatId })
                .IsUnique()
                .HasFilter($"{hostOnly} AND {noTopic}")
                .HasDatabaseName("IX_ChannelThreadMapping_Host_Channel_Chat_NoTopic");
        }
        else
        {
            builder.HasIndex(e => new { e.ChannelName, e.ChatId, e.TopicId })
                .IsUnique()
                .HasFilter(topicSet)
                .HasDatabaseName("IX_ChannelThreadMapping_Channel_Chat_Topic");
            builder.HasIndex(e => new { e.ChannelName, e.ChatId })
                .IsUnique()
                .HasFilter(noTopic)
                .HasDatabaseName("IX_ChannelThreadMapping_Channel_Chat_NoTopic");
        }

        // 快速按 ThreadId 查找
        builder.HasIndex(e => e.ThreadId)
            .HasDatabaseName("IX_ChannelThreadMapping_ThreadId");
    }
}
