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

        // 列宽与 AuditMiddleware 的采集侧截断共用 AuditOperationColumns 这一份数字：
        // 一行超列宽会让整批 INSERT 被拒、整批审计丢失，见该类型注释。
        builder.Property(e => e.FunctionName).IsRequired().HasMaxLength(AuditOperationColumns.FunctionNameMaxLength);
        builder.Property(e => e.PermissionName).HasMaxLength(AuditOperationColumns.PermissionNameMaxLength);
        builder.Property(e => e.UserName).HasMaxLength(AuditOperationColumns.UserNameMaxLength);
        builder.Property(e => e.NickName).HasMaxLength(AuditOperationColumns.NickNameMaxLength);
        builder.Property(e => e.Ip).HasMaxLength(AuditOperationColumns.IpMaxLength);
        builder.Property(e => e.OperatingSystem).HasMaxLength(AuditOperationColumns.OperatingSystemMaxLength);
        builder.Property(e => e.Browser).HasMaxLength(AuditOperationColumns.BrowserMaxLength);
        builder.Property(e => e.UserAgent).HasMaxLength(AuditOperationColumns.UserAgentMaxLength);
        builder.Property(e => e.Message).HasMaxLength(AuditOperationColumns.MessageMaxLength);
        builder.Property(e => e.HttpMethod).HasMaxLength(AuditOperationColumns.HttpMethodMaxLength);
        builder.Property(e => e.Url).HasMaxLength(AuditOperationColumns.UrlMaxLength);
        builder.Property(e => e.Exception);
        builder.Property(e => e.RequestParameters);
        builder.Property(e => e.RequestBody).HasMaxLength(AuditOperationColumns.RequestBodyMaxLength);
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
