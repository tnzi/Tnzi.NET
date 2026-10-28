using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 生成记录表上那几条最热的查询（按状态找失败期次、按模板找失败期次与历史）要有<b>不带过滤</b>的索引可走。
/// </summary>
/// <remarks>
/// 幂等键是过滤索引（<c>Status &lt;&gt; Failed</c>），而 <c>Status = Failed</c> 与过滤条件矛盾、按模板列历史也不蕴含它，
/// 所以那条索引帮不上忙；本表没有导航与外键，EF 也不会自动补 <c>RecurringDocumentId</c> 的索引。
/// </remarks>
public class RecurringRunIndexTests : FinanceIntegrationTestBase
{
    private IReadOnlyList<IIndex> Indexes()
        => [.. DbContext.Model.FindEntityType(typeof(RecurringRun))!.GetIndexes()];

    private static bool StartsWith(IIndex index, params string[] columns)
    {
        var names = index.Properties.Select(p => p.Name).Where(n => n != nameof(RecurringRun.TenantId)).ToList();
        return names.Count >= columns.Length && names.Take(columns.Length).SequenceEqual(columns);
    }

    [Fact]
    public void ThereIsAnUnfilteredIndexLedByTheTemplate_CoveringStatus()
    {
        Indexes().ShouldContain(i => i.GetFilter() == null
            && StartsWith(i, nameof(RecurringRun.RecurringDocumentId), nameof(RecurringRun.Status)));
    }

    [Fact]
    public void ThereIsAnUnfilteredIndexLedByStatus()
    {
        Indexes().ShouldContain(i => i.GetFilter() == null && StartsWith(i, nameof(RecurringRun.Status)));
    }
}
