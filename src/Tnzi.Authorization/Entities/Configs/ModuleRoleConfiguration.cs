namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// ModuleRole 实体配置类
/// </summary>
public class ModuleRoleConfiguration : EntityTypeConfigurationBase<ModuleRole, Guid>
{
    public override void Configure(EntityTypeBuilder<ModuleRole> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 配置与FunctionModule的关系
        builder.HasOne(e => e.FunctionModule)
            .WithMany()
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.ModuleId, e.RoleId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.ModuleId, e.RoleId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.RoleId);
    }
}

