
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// AccessLog 实体配置类
/// </summary>
public class AccessLogConfiguration : EntityTypeConfigurationBase<AccessLog, Guid>
{
    public override void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CreationTime);
        builder.HasIndex(a => new { a.Path, a.CreationTime });

        // 租户列只在多租户开启时存在：读取面按调用者租户收口要用它（租户内的列表与统计都按时间过滤）。
        // 关闭时 Ignore，单租户部署的表结构与迁移一字不变；读取面在关闭时一行租户谓词都不加。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => new { a.TenantId, a.CreationTime });
        }
        else
        {
            builder.Ignore(a => a.TenantId);
        }

        // 列宽与落库前的截断共用 AccessLogColumns 这一份数字：
        // 一行超列宽会让整批 INSERT 被拒、整批访问日志丢失，见该类型注释。
        builder.Property(a => a.Path).HasMaxLength(AccessLogColumns.PathMaxLength);
        builder.Property(a => a.Method).HasMaxLength(AccessLogColumns.MethodMaxLength);
        builder.Property(a => a.IpAddress).HasMaxLength(AccessLogColumns.IpAddressMaxLength);
        builder.Property(a => a.IpCountry).HasMaxLength(AccessLogColumns.IpCountryMaxLength);
        builder.Property(a => a.IpProvince).HasMaxLength(AccessLogColumns.IpProvinceMaxLength);
        builder.Property(a => a.IpCity).HasMaxLength(AccessLogColumns.IpCityMaxLength);
        builder.Property(a => a.IpDistrict).HasMaxLength(AccessLogColumns.IpDistrictMaxLength);
        builder.Property(a => a.IpIsp).HasMaxLength(AccessLogColumns.IpIspMaxLength);
        builder.Property(a => a.IpFullAddress).HasMaxLength(AccessLogColumns.IpFullAddressMaxLength);
        builder.Property(a => a.UaBrowser).HasMaxLength(AccessLogColumns.UaBrowserMaxLength);
        builder.Property(a => a.UaBrowserVersion).HasMaxLength(AccessLogColumns.UaBrowserVersionMaxLength);
        builder.Property(a => a.UaOperatingSystem).HasMaxLength(AccessLogColumns.UaOperatingSystemMaxLength);
        builder.Property(a => a.UaOperatingSystemVersion).HasMaxLength(AccessLogColumns.UaOperatingSystemVersionMaxLength);
        builder.Property(a => a.UaDeviceType).HasMaxLength(AccessLogColumns.UaDeviceTypeMaxLength);
        builder.Property(a => a.UaDeviceBrand).HasMaxLength(AccessLogColumns.UaDeviceBrandMaxLength);
        builder.Property(a => a.UaDeviceModel).HasMaxLength(AccessLogColumns.UaDeviceModelMaxLength);
    }
}
