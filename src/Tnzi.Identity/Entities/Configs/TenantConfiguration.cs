namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// Tenant 实体配置类
/// </summary>
public class TenantConfiguration : EntityTypeConfigurationBase<Tenant, Guid>
{
    /// <summary>
    /// 租户主数据只在多租户开启时建表；单租户模式下 <c>IdentityDbContext</c> 会忽略 <see cref="Tenant"/>，
    /// 这里先映射它只会换来一条「先显式映射、后又忽略」的模型校验警告。
    /// </summary>
    protected override bool IsEnabledFor(DbContext? dbContext)
        => (dbContext as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

    public override void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Code).HasMaxLength(64);
        builder.Property(t => t.Remark).HasMaxLength(500);
        builder.Property(t => t.IsEnabled).IsRequired().HasDefaultValue(true);

        builder.HasIndex(t => t.Name);
        builder.HasIndex(t => t.IsEnabled);
        builder.HasIndex(t => t.ExpiredAt);
        builder.HasIndex(t => t.Code)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
    }
}
