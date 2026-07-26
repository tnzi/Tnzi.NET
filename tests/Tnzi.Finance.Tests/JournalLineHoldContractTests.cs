using Moq;
using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 会计内核与银行域之间那两个解耦契约的**可选性**不变量。
/// </summary>
/// <remarks>
/// 这两个契约存在的唯一理由，是让银行域能整体搬进 <c>Tnzi.Finance.Banking</c> 而内核一行不改。
/// 所以真正要锁死的不是"有实现时守卫仍然生效"（那由既有的
/// <c>ReversalReconciliationGuardTests</c> / <c>BankFeedTests</c> 在真库上覆盖），而是
/// **没有实现时内核仍然能构造、且退回到引入契约之前的行为** —— 否则纯会计消费方
/// 一旦不加载银行域，冲销守卫要么崩、要么静默改变判定。
/// </remarks>
public class JournalLineHoldContractTests
{
    /// <summary>
    /// 守卫在没有任何持有者实现时必须能构造：拒绝只由实现贡献，缺省绝不是"默认拒绝"。
    /// </summary>
    [Fact]
    public void ReversalGuard_ConstructsWithoutAnyHoldProvider()
    {
        var guard = new ReversalGuard(
            Mock.Of<IFiscalYearService>(),
            Mock.Of<IReadOnlyRepository<ReconciliationLine, Guid>>(),
            Mock.Of<IReadOnlyRepository<Reconciliation, Guid>>(),
            holdProviders: null);

        guard.ShouldNotBeNull();
    }

    /// <summary>
    /// 空入参不查库：守卫在凭证无行时也会被调到，那时不该白跑一次往返。
    /// </summary>
    /// <remarks>Strict mock：任何一次仓储调用都会让本例失败。</remarks>
    [Fact]
    public async Task HoldProvider_EmptyInput_ReturnsEmpty_WithoutQuerying()
    {
        var repository = new Mock<IReadOnlyRepository<BankTransaction, Guid>>(MockBehavior.Strict);
        var provider = new BankStatementHoldProvider(repository.Object);

        var holds = await provider.GetHoldsAsync(Array.Empty<Guid>());

        holds.ShouldBeEmpty();
        repository.VerifyNoOtherCalls();
    }

    /// <summary>关键字为空/空白时不查支票登记簿。</summary>
    [Fact]
    public async Task SearchContributor_BlankKeyword_ReturnsEmpty_WithoutQuerying()
    {
        var repository = new Mock<IReadOnlyRepository<BankCheck, Guid>>(MockBehavior.Strict);
        var contributor = new CheckNumberSearchContributor(repository.Object);

        (await contributor.MatchAsync(string.Empty)).ShouldBeEmpty();
        (await contributor.MatchAsync("   ")).ShouldBeEmpty();
        repository.VerifyNoOtherCalls();
    }

    /// <summary>
    /// 贡献者用 <see cref="FinanceSourceTypes"/> 的**字符串常量**而不是 <c>nameof</c>：
    /// 来源令牌是 wire 契约，实体改名后 nameof 版本编译照过却会静默改变存量数据的匹配。
    /// </summary>
    [Fact]
    public void SearchMatch_UsesTheWireSourceTypeConstant()
    {
        var match = new GeneralLedgerSourceMatch(FinanceSourceTypes.PaymentEntry, Guid.NewGuid().ToString());
        match.SourceType.ShouldBe("PaymentEntry");
    }

    /// <summary>持有事实携带 wire 原因代码，供呈现端做分支而不是解析英文句子。</summary>
    [Fact]
    public void Hold_CarriesTheWireReasonCode()
    {
        var hold = new JournalLineHold(Guid.NewGuid(), ReversalBlockReasons.StatementMatched, "detail");
        hold.ReasonCode.ShouldBe("statementMatched");
    }
}
