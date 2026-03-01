
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// AccessLog 实体配置类
/// </summary>
public class AccessLogConfiguration : EntityTypeConfigurationBase<AccessLog, Guid>
{
    public override void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CreationTime);
        builder.HasIndex(a => new { a.Path, a.CreationTime });

        builder.Property(a => a.Path).HasMaxLength(500);
        builder.Property(a => a.Method).HasMaxLength(10);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.IpCountry).HasMaxLength(50);
        builder.Property(a => a.IpProvince).HasMaxLength(50);
        builder.Property(a => a.IpCity).HasMaxLength(50);
        builder.Property(a => a.IpDistrict).HasMaxLength(50);
        builder.Property(a => a.IpIsp).HasMaxLength(100);
        builder.Property(a => a.IpFullAddress).HasMaxLength(200);
        builder.Property(a => a.UaBrowser).HasMaxLength(50);
        builder.Property(a => a.UaBrowserVersion).HasMaxLength(30);
        builder.Property(a => a.UaOperatingSystem).HasMaxLength(50);
        builder.Property(a => a.UaOperatingSystemVersion).HasMaxLength(30);
        builder.Property(a => a.UaDeviceType).HasMaxLength(30);
        builder.Property(a => a.UaDeviceBrand).HasMaxLength(50);
        builder.Property(a => a.UaDeviceModel).HasMaxLength(50);
    }
}
