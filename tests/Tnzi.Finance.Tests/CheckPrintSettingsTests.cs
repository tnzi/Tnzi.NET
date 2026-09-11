namespace Tnzi.Finance.Tests;

/// <summary>
/// 已开支票的打印设置快照（<see cref="CheckPrintSettings"/>）的回退语义
/// </summary>
/// <remarks>
/// 这组断言守的是本机制的两条边：
/// ①<b>有快照就必须钉住</b>——换过模板 / 调过偏移之后重新渲染一张历史支票，
///   画出来的仍是当初那张纸；否则纸悄悄变了而没有任何报错；
/// ②<b>没有快照必须与引入前逐字相同</b>——存量支票与手工登记的支票五列全空，
///   整条退回当前银行档案，消费应用只加列、不必回填任何数据。
/// </remarks>
public class CheckPrintSettingsTests
{
    [Fact]
    public void IssuedCheckWithASnapshot_PinsEveryPrintSetting()
    {
        var bank = BuildBank();
        var check = new BankCheck
        {
            PrintTemplateName = "check-voucher-bottom-us",
            PrintLayout = CheckLayout.ThreePerPage,
            PrintStockType = CheckStockType.Blank,
            PrintOffsetXMm = -1.5m,
            PrintOffsetYMm = 2.25m
        };

        // 档案在开票之后被改过：模板、版式、票纸、两个偏移全变了
        var settings = CheckPrintSettings.ForIssuedCheck(check, bank);

        settings.TemplateName.ShouldBe("check-voucher-bottom-us");
        settings.Layout.ShouldBe(CheckLayout.ThreePerPage);
        settings.StockType.ShouldBe(CheckStockType.Blank);
        settings.OffsetXMm.ShouldBe(-1.5m);
        settings.OffsetYMm.ShouldBe(2.25m);
    }

    [Fact]
    public void LegacyCheckWithoutASnapshot_BehavesExactlyLikeTheCurrentBankProfile()
    {
        var bank = BuildBank();

        // 存量行：五列全空（这次改动之前开出的票，以及手工登记的票）
        var settings = CheckPrintSettings.ForIssuedCheck(new BankCheck(), bank);

        settings.ShouldBe(CheckPrintSettings.FromBank(bank));
    }

    [Fact]
    public void PartialSnapshot_FallsBackFieldByField()
    {
        var bank = BuildBank();
        var check = new BankCheck { PrintTemplateName = "check-3up" };

        var settings = CheckPrintSettings.ForIssuedCheck(check, bank);

        settings.TemplateName.ShouldBe("check-3up");
        settings.Layout.ShouldBe(bank.CheckLayout);
        settings.StockType.ShouldBe(bank.CheckStockType);
        settings.OffsetXMm.ShouldBe(bank.OffsetXMm);
        settings.OffsetYMm.ShouldBe(bank.OffsetYMm);
    }

    [Fact]
    public void ZeroOffsetSnapshot_IsNotMistakenForAMissingOne()
    {
        // 0 是一个真实的偏移值。用 ?? 而不是「非零才算数」正是为了这条：
        // 档案后来被调成 3mm，而当初那张纸确实是零偏移打出来的。
        var bank = BuildBank();
        bank.OffsetXMm = 3m;
        bank.OffsetYMm = 3m;

        var settings = CheckPrintSettings.ForIssuedCheck(
            new BankCheck { PrintOffsetXMm = 0m, PrintOffsetYMm = 0m }, bank);

        settings.OffsetXMm.ShouldBe(0m);
        settings.OffsetYMm.ShouldBe(0m);
    }

    [Fact]
    public void BlankTemplateOverride_KeepsTheBankProfileTemplate()
    {
        // 一个没填的可选字段不该把账户上配好的版式换掉
        var settings = CheckPrintSettings.FromBank(BuildBank());

        settings.WithTemplateOverride(null).TemplateName.ShouldBe("check-cpa006-ca");
        settings.WithTemplateOverride("   ").TemplateName.ShouldBe("check-cpa006-ca");
    }

    [Fact]
    public void TemplateOverride_ReplacesTheTemplateAndNothingElse()
    {
        var settings = CheckPrintSettings.FromBank(BuildBank());

        var overridden = settings.WithTemplateOverride("  check-3up  ");

        overridden.TemplateName.ShouldBe("check-3up");
        overridden.ShouldBe(settings with { TemplateName = "check-3up" });
    }

    private static BankAccount BuildBank() => new()
    {
        Name = "Operating account",
        CheckTemplateName = "check-cpa006-ca",
        CheckLayout = CheckLayout.Voucher,
        CheckStockType = CheckStockType.PrePrinted,
        OffsetXMm = 0.5m,
        OffsetYMm = -0.25m
    };
}
