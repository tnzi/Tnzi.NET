namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// ModuleUser 实体配置类
/// </summary>
public class ModuleUserConfiguration : EntityTypeConfigurationBase<ModuleUser, Guid>
{
    public override void Configure(EntityTypeBuilder<ModuleUser> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(e => e.Id);

        // 配置与FunctionModule的关系
        builder.HasOne(e => e.FunctionModule)
            .WithMany()
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => new { e.ModuleId, e.UserId }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.UserId);
    }
}

