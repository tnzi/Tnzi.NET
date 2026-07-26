namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 账本封账锁配置（每租户单行）
/// </summary>
public class LedgerLockConfiguration : EntityTypeConfigurationBase<LedgerLock, Guid>
{
    public override void Configure(EntityTypeBuilder<LedgerLock> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Scope).HasMaxLength(16).IsRequired();
        builder.Property(e => e.PasswordHash).HasMaxLength(256);
        builder.Property(e => e.Note).HasMaxLength(500);

        // 每租户单行：唯一索引兜底服务层的 get-or-create，杜绝并发插出两条封账日
        // （随后校验读到哪一条全看运气）。单租户下 TenantId 未映射，故按判别列单列建。
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Scope }).IsUnique().HasFilter(notDeleted);
        }
        else
        {
            builder.HasIndex(e => e.Scope).IsUnique().HasFilter(notDeleted);
        }
    }
}
