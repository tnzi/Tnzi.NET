namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// FunctionModule 实体配置类
/// </summary>
public class FunctionModuleConfiguration : EntityTypeConfigurationBase<FunctionModule, Guid>
{
    public override void Configure(EntityTypeBuilder<FunctionModule> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);

        // 配置父子关系
        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 配置与ModuleFunction的关系
        builder.HasMany(e => e.Functions)
            .WithOne(e => e.FunctionModule)
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => e.Code).IsUnique()
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
        builder.HasIndex(e => e.ParentId);
    }
}
