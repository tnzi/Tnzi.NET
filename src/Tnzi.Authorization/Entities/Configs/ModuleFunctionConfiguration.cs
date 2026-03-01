namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// ModuleFunction 实体配置类
/// </summary>
public class ModuleFunctionConfiguration : EntityTypeConfigurationBase<ModuleFunction, Guid>
{
    public override void Configure(EntityTypeBuilder<ModuleFunction> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);

        // 创建索引
        builder.HasIndex(e => new { e.ModuleId, e.Code }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.ModuleId);
    }
}

