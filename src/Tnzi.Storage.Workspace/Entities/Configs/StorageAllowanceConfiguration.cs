namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// StorageAllowance 实体配置
/// </summary>
public class StorageAllowanceConfiguration : EntityTypeConfigurationBase<StorageAllowance, Guid>
{
    public override void Configure(EntityTypeBuilder<StorageAllowance> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("Allowance");

        builder.Property(e => e.Note).HasMaxLength(512);

        // 每个用户至多一条覆盖值。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.UserId).IsUnique();
        }
    }
}
