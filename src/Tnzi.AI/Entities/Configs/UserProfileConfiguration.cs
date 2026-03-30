namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// UserProfile 实体配置类
/// </summary>
public class UserProfileConfiguration : EntityTypeConfigurationBase<UserProfile, Guid>
{
    public override void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);

        builder.Property(e => e.Role)
            .HasMaxLength(500);

        builder.Property(e => e.PreferredLanguage)
            .HasMaxLength(20);

        builder.Property(e => e.Content)
            .HasMaxLength(16000);

        // 唯一索引：(TenantId, UserId)
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId })
                .IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.UserId)
                .IsUnique();
        }
    }
}
