
namespace Tnzi.EFCore.DocumentNumbering;

/// <summary>
/// 单据序列配置（表名无模块前缀，映射为裸 <c>DocumentSequence</c>）
/// </summary>
public class DocumentSequenceConfiguration : EntityTypeConfigurationBase<DocumentSequence, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(s => s.Scope).HasMaxLength(64).IsRequired();

        // 无软删除实体，唯一索引不需要过滤器
        if (multiTenancyEnabled)
        {
            builder.HasIndex(s => new { s.TenantId, s.Scope }).IsUnique();
        }
        else
        {
            builder.HasIndex(s => s.Scope).IsUnique();
        }
    }
}
