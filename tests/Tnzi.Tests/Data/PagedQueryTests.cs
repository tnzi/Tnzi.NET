namespace Tnzi.Tests.Data;

/// <summary>
/// <see cref="PagedQuery"/> / <see cref="PagedQueryDto"/>：<c>Skip</c> / <c>Take</c> 与回报给调用方的
/// <c>PageIndex</c> / <c>PageSize</c> 必须是同一个值，否则响应宣称的页长与实际取数不一致，后半段记录永远翻不到。
/// </summary>
public class PagedQueryTests
{
    private sealed class TwentyPerPageQuery : PagedQueryDto
    {
        protected override int DefaultPageSize => 20;
    }

    private sealed class UnlimitedQuery : PagedQueryDto
    {
        protected override int MaxPageSize => 0;
    }

    private sealed class FiftyMaxQuery : PagedQueryDto
    {
        protected override int MaxPageSize => 50;
    }

    [Fact]
    public void DerivedDefaultPageSize_WhenPageSizeIsNeverAssigned_DrivesTakeAndSkip()
    {
        var query = new TwentyPerPageQuery();

        Assert.Equal(20, query.PageSize);
        Assert.Equal(20, query.Take);
        Assert.Equal(0, query.Skip);
    }

    [Fact]
    public void DerivedDefaultPageSize_WithOnlyPageIndexAssigned_SkipsWholePages()
    {
        var query = new TwentyPerPageQuery { PageIndex = 2 };

        Assert.Equal(20, query.Skip);
        Assert.Equal(20, query.Take);
    }

    [Fact]
    public void DerivedDefaultPageSize_SeenThroughTheBaseType_IsTheSameValue()
    {
        PagedQuery query = new TwentyPerPageQuery { PageIndex = 3 };

        Assert.Equal(20, query.PageSize);
        Assert.Equal(3, query.PageIndex);
        Assert.Equal(40, query.Skip);
    }

    [Fact]
    public void MaxPageSizeZero_MeansNoLimit()
    {
        var query = new UnlimitedQuery { PageSize = 500 };

        Assert.Equal(500, query.PageSize);
        Assert.Equal(500, query.Take);
    }

    [Fact]
    public void DerivedMaxPageSize_ClampsPageSizeAndTake()
    {
        var query = new FiftyMaxQuery { PageSize = 80 };

        Assert.Equal(50, query.PageSize);
        Assert.Equal(50, query.Take);
    }

    [Fact]
    public void BasePagedQuery_OverTheLimit_IsClampedToTheLimitNotResetToTheDefault()
    {
        var query = new PagedQuery { PageSize = 150 };

        Assert.Equal(100, query.PageSize);
        Assert.Equal(100, query.Take);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveValues_FallBackToTheDefaults(int value)
    {
        var query = new TwentyPerPageQuery { PageIndex = value, PageSize = value };

        Assert.Equal(1, query.PageIndex);
        Assert.Equal(20, query.PageSize);
        Assert.Equal(0, query.Skip);
    }

    [Fact]
    public void JsonBody_WithoutPageSize_UsesTheDerivedDefault()
    {
        var query = JsonSerializer.Deserialize<TwentyPerPageQuery>("""{"pageIndex":2}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(20, query.PageSize);
        Assert.Equal(20, query.Skip);
    }

    [Fact]
    public void PagedListCreate_FromAQueryWithoutPageSize_ReportsWhatItTook()
    {
        var query = new TwentyPerPageQuery { PageIndex = 2 };
        var source = Enumerable.Range(1, 40).ToList();

        var page = source.Skip(query.Skip).Take(query.Take).ToList();
        var paged = new PagedList<int>(page, query.PageIndex, query.PageSize, source.Count);

        Assert.Equal(paged.PageSize, paged.Items.Count);
        Assert.Equal(21, paged.Items[0]);
        Assert.Equal(2, paged.TotalPages);
    }
}
