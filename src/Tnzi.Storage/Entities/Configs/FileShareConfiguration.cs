namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileShare 实体配置类
/// </summary>
public class FileShareConfiguration : EntityTypeConfigurationBase<FileShare, Guid>
{
    public override void Configure(EntityTypeBuilder<FileShare> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("Share");

        builder.Property(e => e.FileId).IsRequired();
        builder.Property(e => e.ShareToken).IsRequired().HasMaxLength(64);
        builder.Property(e => e.PasswordHash).HasMaxLength(256);
        builder.Property(e => e.IsEnabled).HasDefaultValue(true);
        builder.Property(e => e.CreationTime).IsRequired();

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.FileId);
        builder.HasIndex(e => e.ShareToken).IsUnique();
        builder.HasIndex(e => e.IsEnabled);
        builder.HasIndex(e => e.ExpiresAt);
        builder.HasIndex(e => e.CreationTime);
    }
}
