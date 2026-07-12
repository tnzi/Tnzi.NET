namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// UserFunction 实体配置类
/// </summary>
public class UserFunctionConfiguration : EntityTypeConfigurationBase<UserFunction, Guid>
{
    public override void Configure(EntityTypeBuilder<UserFunction> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 配置与ModuleFunction的关系
        builder.HasOne(e => e.Function)
            .WithMany()
            .HasForeignKey(e => e.FunctionId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId, e.FunctionId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.UserId, e.FunctionId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.FunctionId);
    }
}
