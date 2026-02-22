namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// RoleFunction 实体配置类
/// </summary>
public class RoleFunctionConfiguration : EntityTypeConfigurationBase<RoleFunction, Guid>
{
    public override void Configure(EntityTypeBuilder<RoleFunction> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(e => e.Id);

        // 配置与ModuleFunction的关系
        builder.HasOne(e => e.Function)
            .WithMany()
            .HasForeignKey(e => e.FunctionId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => new { e.RoleId, e.FunctionId }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.RoleId);
        builder.HasIndex(e => e.FunctionId);
    }
}

