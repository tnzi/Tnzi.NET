using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 系统派生、收进列宽的文本（凭证摘要、银行流水摘要与收款人、周期单据的失败原因）：截断点不落在代理对中间。
/// </summary>
/// <remarks>
/// 这些值都按 UTF-16 码元收进列宽后落库。孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入：
/// 过账答 500、整批流水导入失败、失败留痕写不进去。SQLite 不检查，所以这里直接断言截出来的字符串。
/// </remarks>
public class DerivedTextTruncationTests
{
    /// <summary>第 <paramref name="width"/> 个码元是 emoji 的前半个。</summary>
    private static string EmojiAtTheCut(int width) => new string('a', width - 1) + "\U0001F600tail";

    [Fact]
    public void FinanceFieldLimits_Fit_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        FinanceFieldLimits.FitJournalMemo(EmojiAtTheCut(FinanceFieldLimits.MemoMaxLength))
            .ShouldBe(new string('a', FinanceFieldLimits.MemoMaxLength - 1));
    }

    [Fact]
    public void BankTransactionFieldLimits_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        BankTransactionFieldLimits.ClampDescription(EmojiAtTheCut(BankTransactionFieldLimits.DescriptionMaxLength))
            .ShouldBe(new string('a', BankTransactionFieldLimits.DescriptionMaxLength - 1));
        BankTransactionFieldLimits.ClampPayee(EmojiAtTheCut(BankTransactionFieldLimits.PayeeMaxLength))
            .ShouldBe(new string('a', BankTransactionFieldLimits.PayeeMaxLength - 1));
    }

    [Fact]
    public void RecurringFailReason_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        RecurringGeneratorService.FitFailReason(EmojiAtTheCut(RecurringGeneratorService.FailReasonMaxLength))
            .ShouldBe(new string('a', RecurringGeneratorService.FailReasonMaxLength - 1));
    }
}
