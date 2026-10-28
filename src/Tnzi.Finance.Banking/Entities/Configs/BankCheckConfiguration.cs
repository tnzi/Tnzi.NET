namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 支票记录配置
/// </summary>
public class BankCheckConfiguration : EntityTypeConfigurationBase<BankCheck, Guid>
{
    public override void Configure(EntityTypeBuilder<BankCheck> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.PayeeName).HasMaxLength(BankingFieldLimits.CheckPayeeNameMaxLength);
        builder.Property(e => e.Currency).HasMaxLength(BankingFieldLimits.CurrencyMaxLength);
        builder.Property(e => e.VoidReason).HasMaxLength(BankingFieldLimits.VoidReasonMaxLength);
        builder.Property(e => e.Amount).HasMoneyPrecision();

        // 打印设置快照：长度与精度对齐它们的来源列（BankAccount.CheckTemplateName / OffsetXMm / OffsetYMm），
        // 否则同一个值在档案上存得下、抄进快照时被截断。
        builder.Property(e => e.PrintTemplateName).HasMaxLength(BankingFieldLimits.CheckTemplateNameMaxLength);
        builder.Property(e => e.PrintOffsetXMm).HasMoneyPrecision();
        builder.Property(e => e.PrintOffsetYMm).HasMoneyPrecision();

        // 同一银行账户内支票号唯一（占号留痕；显式号撞号由唯一索引兜底翻译 409）
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.BankAccountId, e.CheckNumber }).IsUnique().HasFilter(notDeleted);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.BankAccountId, e.CheckNumber }).IsUnique().HasFilter(notDeleted);
        }

        builder.HasIndex(e => new { e.BankAccountId, e.Status });
        builder.HasIndex(e => e.PaymentEntryId);

        // ★ 一笔付款至多一张有效（Issued）支票。打印 / 登记的「已开过票」检查都在事务外读，
        // 两个并发请求（双击、超时重试、两个出纳打同一项）会各自通过、各分配一个号，
        // 登记簿上留下两张可流通的票、positive-pay 两张都报，收款方两张都能兑。
        // 打印在号码行锁之后会再查一次；这条索引是兜底，也覆盖不经号码行锁的手工登记。
        // 作废 / 毁票的行不受约束（重打 = 原票作废 + 新票 Issued，同一付款的历史链是合法的）。
        // 已有库在建这条索引之前若已有同一付款的两张 Issued 支票，建索引会失败：先查出并作废多出来的那张
        //（查询与处置写在 docs/upgrade-notes.md 的 2026-10-12 Finance 补记一节）。
        var issuedForPayment = IndexFilter.EqualTo(nameof(BankCheck.Status), (int)CheckStatus.Issued)
            .And(IndexFilter.NotNull(nameof(BankCheck.PaymentEntryId)))
            .AndNotDeleted()
            .Build();
        if (multiTenancyEnabled)
            builder.HasIndex(e => new { e.TenantId, e.PaymentEntryId, e.Status }).IsUnique().HasFilter(issuedForPayment);
        else
            builder.HasIndex(e => new { e.PaymentEntryId, e.Status }).IsUnique().HasFilter(issuedForPayment);
    }
}
