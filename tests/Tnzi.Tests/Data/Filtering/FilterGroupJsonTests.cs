namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// <see cref="FilterGroupJson"/>：过滤器 JSON 的唯一方言，三种失败各自可区分。
/// </summary>
public class FilterGroupJsonTests
{
    /// <summary>列表 API 的 camelCase + 字符串枚举形态必须原样读得出规则。</summary>
    [Fact]
    public void TryParse_CamelCaseWithStringEnums_YieldsRules()
    {
        const string json = """{"logic":"Or","rules":[{"field":"OwnerId","operator":"Equal","value":"abc"}]}""";

        var ok = FilterGroupJson.TryParse(json, out var group, out var error);

        Assert.True(ok, error);
        Assert.NotNull(group);
        Assert.Equal(LogicalOperator.Or, group!.Logic);
        var rule = Assert.Single(group.Rules);
        Assert.Equal("OwnerId", rule.Field);
        Assert.Equal(FilterOperator.Equal, rule.Operator);
        Assert.True(group.HasFilters);
    }

    [Fact]
    public void TryParse_PascalCaseWithNumericEnums_YieldsRules()
    {
        const string json = """{"Logic":0,"Rules":[{"Field":"Name","Operator":2,"Value":"x"}]}""";

        Assert.True(FilterGroupJson.TryParse(json, out var group, out _));

        Assert.Single(group!.Rules);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"rules":[{"field":"A","operator":"NoSuchOperator"}]}""")]
    public void TryParse_MalformedJson_FailsWithReason(string json)
    {
        Assert.False(FilterGroupJson.TryParse(json, out var group, out var error));

        Assert.Null(group);
        Assert.NotNull(error);
        Assert.StartsWith("Filter JSON is malformed", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Empty_Fails(string? json)
    {
        Assert.False(FilterGroupJson.TryParse(json, out var group, out var error));

        Assert.Null(group);
        Assert.Equal("Filter JSON is empty.", error);
    }

    [Fact]
    public void TryParse_LiteralNull_Fails()
    {
        Assert.False(FilterGroupJson.TryParse("null", out var group, out var error));

        Assert.Null(group);
        Assert.Equal("Filter JSON is the literal null.", error);
    }

    /// <summary>「写了却没规则」不是解析失败：成功返回但 HasFilters=false，由调用方决定。</summary>
    [Fact]
    public void TryParse_ObjectWithoutRules_SucceedsWithNoFilters()
    {
        Assert.True(FilterGroupJson.TryParse("""{"rule":[{"field":"A"}]}""", out var group, out _));

        Assert.False(group!.HasFilters);
    }
}
