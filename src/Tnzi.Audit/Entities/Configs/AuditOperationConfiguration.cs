namespace Tnzi.Audit.Entities.Configs;

/// <summary>
/// AuditOperation 实体配置类
/// </summary>
public class AuditOperationConfiguration : EntityTypeConfigurationBase<AuditOperation, Guid>
{
    public override void Configure(EntityTypeBuilder<AuditOperation> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("Operation");

        builder.Property(e => e.FunctionName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PermissionName).HasMaxLength(200);
        builder.Property(e => e.UserName).HasMaxLength(200);
        builder.Property(e => e.NickName).HasMaxLength(200);
        builder.Property(e => e.Ip).HasMaxLength(50);
        builder.Property(e => e.OperatingSystem).HasMaxLength(200);
        builder.Property(e => e.Browser).HasMaxLength(200);
        builder.Property(e => e.UserAgent).HasMaxLength(500);
        builder.Property(e => e.Message).HasMaxLength(2000);
        builder.Property(e => e.HttpMethod).HasMaxLength(10);
        builder.Property(e => e.Url).HasMaxLength(2000);
        builder.Property(e => e.Exception);
        builder.Property(e => e.RequestParameters);
        builder.Property(e => e.RequestBody).HasMaxLength(8192);
        builder.Property(e => e.ResponseResult);

        // 配置与 AuditEntityEntry 的关系
        builder.HasMany(e => e.EntityEntries)
            .WithOne(e => e.AuditOperation)
            .HasForeignKey(e => e.AuditOperationId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => e.UserId);
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.Ignore(e => e.TenantId);
        }
        builder.HasIndex(e => e.StartTime);
        builder.HasIndex(e => e.FunctionName);
        builder.HasIndex(e => e.PermissionName);
        builder.HasIndex(e => new { e.UserId, e.StartTime });
        // Operations/Logs 视图按 IsWrite 过滤 + StartTime 排序
        builder.HasIndex(e => new { e.IsWrite, e.StartTime });
    }
}
