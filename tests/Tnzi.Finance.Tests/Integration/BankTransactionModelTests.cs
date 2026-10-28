using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行流水表上那些「每一次作废都会查」的列必须有索引。
/// </summary>
/// <remarks>
/// <c>BankStatementHoldProvider</c> 回答冲销守卫「这一行总账被银行流水匹配着吗」，查询只带
/// <c>MatchedJournalLineId</c>、不带科目；匹配候选的反连接也按它查。这张表只增不减，
/// 没有索引时任何单据作废的延迟都随历年导入的流水量增长。
/// </remarks>
public class BankTransactionModelTests : FinanceIntegrationTestBase
{
    [Fact]
    public void MatchedJournalLineId_IsIndexed_SkippingUnmatchedRows()
    {
        var entity = DbContext.Model.FindEntityType(typeof(BankTransaction))!;

        var index = entity.GetIndexes().SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(BankTransaction.MatchedJournalLineId)]));

        index.ShouldNotBeNull("no index leads with MatchedJournalLineId");
        index!.IsUnique.ShouldBeFalse();
        index.GetFilter().ShouldNotBeNull();
        index.GetFilter()!.ShouldContain(nameof(BankTransaction.MatchedJournalLineId));
    }
}
