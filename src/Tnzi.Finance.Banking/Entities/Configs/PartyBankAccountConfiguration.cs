namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 往来方银行账户配置
/// </summary>
public class PartyBankAccountConfiguration : EntityTypeConfigurationBase<PartyBankAccount, Guid>
{
    public override void Configure(EntityTypeBuilder<PartyBankAccount> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Label).HasMaxLength(64);
        builder.Property(e => e.BankName).HasMaxLength(128);
        builder.Property(e => e.RoutingNumber).HasMaxLength(16);
        builder.Property(e => e.InstitutionNumber).HasMaxLength(8);
        builder.Property(e => e.TransitNumber).HasMaxLength(8);
        builder.Property(e => e.AccountNumberEncrypted).HasMaxLength(512);
        builder.Property(e => e.AccountNumberMasked).HasMaxLength(32);
        builder.Property(e => e.Currency).HasMaxLength(8);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.PartyType, e.PartyId });

        // 每往来方至多一个默认账户（DB 兜底跨行不变量）：并发 SetDefault/Create(IsDefault=true) 会各自
        // 置 self=true 提交出两条默认，EftService 用 .First() 任取其一 → 错投银行账户。
        // 过滤唯一索引把该竞态收口到 409，对齐 BankAccount/Reconciliation-draft 加固先例。
        // ★须用命名重载 HasIndex(expr, name)：EF 按属性集索引，单租户下不给独立名会与上面通用
        // (PartyType,PartyId) 索引合并为同一对象、丢掉通用查询索引。命名重载才建成独立第二索引。
        var defaultTrueNotDeleted = IndexFilterFactory.GetColumnTrueAndIsDeletedFalse("IsDefault");
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.PartyType, e.PartyId }, "UX_Finance_PartyBankAccount_Default")
                .IsUnique().HasFilter(defaultTrueNotDeleted);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.PartyType, e.PartyId }, "UX_Finance_PartyBankAccount_Default")
                .IsUnique().HasFilter(defaultTrueNotDeleted);
        }
    }
}
