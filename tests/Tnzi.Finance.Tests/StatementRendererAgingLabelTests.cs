using System.Text;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 对账单账龄表头必须由生效切分点生成：桶已参数化（Finance:AgingBucketDays），
/// 写死 1-30/31-60/61-90/90+ 会在配了 [7,14,21] 的部署里把「逾期 15 天」
/// 印在标着 31-60 的列下面——纸上的谎最难收回。
/// </summary>
public class StatementRendererAgingLabelTests
{
    private static CustomerStatementDto Statement(int[]? cuts = null)
    {
        var buckets = new AgingBucketsDto
        {
            Current = 100m,
            Days1To30 = 50m,
            Total = 150m,
        };
        if (cuts != null)
            buckets.AgingBucketDays = cuts;

        return new CustomerStatementDto
        {
            PartyId = Guid.NewGuid(),
            PartyName = "Acme Supplies Ltd",
            Style = StatementStyle.OpenItem,
            Currency = "USD",
            PeriodFrom = new DateTime(2026, 1, 1),
            PeriodTo = new DateTime(2026, 1, 31),
            ClosingBalance = 150m,
            Buckets = buckets,
        };
    }

    private static async Task<string> RenderAsync(CustomerStatementDto statement)
    {
        var result = await new TemplateStatementRenderer().RenderAsync(statement);
        result.Succeeded.ShouldBeTrue(result.Message);
        return Encoding.UTF8.GetString(result.Data!);
    }

    [Fact]
    public async Task Render_CustomCuts_HeadersFollowTheConfiguredBoundaries()
    {
        var html = await RenderAsync(Statement([7, 14, 21]));

        html.ShouldContain("<th>1-7</th>");
        html.ShouldContain("<th>8-14</th>");
        html.ShouldContain("<th>15-21</th>");
        html.ShouldContain("<th>21+</th>");
        html.ShouldNotContain("1-30");
    }

    [Fact]
    public async Task Render_DefaultCuts_HeadersMatchTheLegacyLabelsExactly()
    {
        // 回归红线：默认切分点下生成的表头与旧版写死的字符串逐字一致
        var html = await RenderAsync(Statement());

        html.ShouldContain("<th>Current</th><th>1-30</th><th>31-60</th><th>61-90</th><th>90+</th>");
    }
}
