namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentThread 实体配置类
/// </summary>
public class AgentThreadConfiguration : EntityTypeConfigurationBase<AgentThread, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentThread> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Title)
            .HasMaxLength(200);

        builder.Property(e => e.Origin)
            .HasMaxLength(ThreadOrigin.MaxLength);

        builder.HasOne(e => e.Agent)
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.LastActivityTime);
        builder.HasIndex(e => e.LastUserMessageTime);

        // 列表与关注计数都按主人 + 在不在列表筛：一条复合索引同时服务两者
        builder.HasIndex(e => new { e.CreatorId, e.Listing });
    }
}
