using System.Linq.Expressions;

namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// 过滤值从 JSON 进来时的转换：<see cref="FilterRule.Value"/> 是 <c>object?</c>，经 System.Text.Json 反序列化后是
/// <see cref="JsonElement"/>。行级数据授权的落库过滤器、列表接口的 <c>[FromBody] PagedQuery</c>、
/// <c>ApplyFilterFromJson</c> 三条入口都走这条路，所以 In / NotIn 与日期必须在这条路上成立。
/// </summary>
public class FilterJsonValueConversionTests
{
    private enum Stage
    {
        Draft = 0,
        Published = 1,
        Archived = 2
    }

    private class Row
    {
        public int Age { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? OwnerId { get; set; }
        public Stage Stage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset ClosedAt { get; set; }
    }

    private static Func<Row, bool> CompileFromJson(string json)
    {
        Assert.True(FilterGroupJson.TryParse(json, out var group, out var error), error);
        return FilterExpressionBuilder.Build<Row>(group).Compile();
    }

    private static FilterGroup ParseJson(string json)
    {
        Assert.True(FilterGroupJson.TryParse(json, out var group, out var error), error);
        return group!;
    }

    private static object? ConstantOf(FilterRule rule)
    {
        var body = FilterExpressionBuilder.Build<Row>(rule).Body;
        var binary = Assert.IsAssignableFrom<BinaryExpression>(body);
        return Assert.IsAssignableFrom<ConstantExpression>(binary.Right).Value;
    }

    private static object? JsonConstantOf(string json)
    {
        var rule = Assert.Single(ParseJson(json).Rules);
        Assert.IsType<JsonElement>(rule.Value);
        return ConstantOf(rule);
    }

    #region In / NotIn

    [Fact]
    public void In_WithJsonNumberArray_FiltersIntProperty()
    {
        var predicate = CompileFromJson("""{"rules":[{"field":"Age","operator":"In","value":[18,20]}]}""");

        Assert.True(predicate(new Row { Age = 18 }));
        Assert.True(predicate(new Row { Age = 20 }));
        Assert.False(predicate(new Row { Age = 19 }));
    }

    [Fact]
    public void In_WithJsonGuidStrings_FiltersNullableGuidProperty()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var predicate = CompileFromJson($$"""{"rules":[{"field":"OwnerId","operator":"In","value":["{{a}}","{{b}}"]}]}""");

        Assert.True(predicate(new Row { OwnerId = a }));
        Assert.True(predicate(new Row { OwnerId = b }));
        Assert.False(predicate(new Row { OwnerId = Guid.NewGuid() }));
        Assert.False(predicate(new Row { OwnerId = null }));
    }

    [Fact]
    public void In_WithJsonEnumNames_FiltersEnumProperty()
    {
        var predicate = CompileFromJson("""{"rules":[{"field":"Stage","operator":"In","value":["Published","archived"]}]}""");

        Assert.True(predicate(new Row { Stage = Stage.Published }));
        Assert.True(predicate(new Row { Stage = Stage.Archived }));
        Assert.False(predicate(new Row { Stage = Stage.Draft }));
    }

    [Fact]
    public void NotIn_WithJsonStringArray_FiltersStringProperty()
    {
        var predicate = CompileFromJson("""{"rules":[{"field":"Name","operator":"NotIn","value":["a","b"]}]}""");

        Assert.False(predicate(new Row { Name = "a" }));
        Assert.True(predicate(new Row { Name = "c" }));
    }

    [Theory]
    [InlineData("""{"rules":[{"field":"Age","operator":"In","value":5}]}""")]
    [InlineData("""{"rules":[{"field":"Name","operator":"In","value":"abc"}]}""")]
    [InlineData("""{"rules":[{"field":"Age","operator":"In","value":{"a":1}}]}""")]
    [InlineData("""{"rules":[{"field":"Age","operator":"In","value":[1,null]}]}""")]
    public void In_WithJsonValueThatIsNotAUsableArray_IsA400(string json)
    {
        var group = ParseJson(json);

        var ex = Assert.Throws<FilterValueException>(() => FilterExpressionBuilder.Build<Row>(group));
        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public void In_WithClrString_IsA400InsteadOfMatchingSingleCharacters()
    {
        // 字符串本身是 IEnumerable<char>：不拦住的话 In "abc" 会按 "a" / "b" / "c" 三个值去匹配
        var ex = Assert.Throws<FilterValueException>(() =>
            FilterExpressionBuilder.Build<Row>(new FilterRule("Name", FilterOperator.In, "abc")));
        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public void Equal_WithJsonArray_IsA400()
    {
        var group = ParseJson("""{"rules":[{"field":"Age","operator":"Equal","value":[1,2]}]}""");

        Assert.Throws<FilterValueException>(() => FilterExpressionBuilder.Build<Row>(group));
    }

    #endregion

    #region 日期按 UTC 解释

    [Theory]
    [InlineData("2026-10-01T00:00:00Z", 0)]
    [InlineData("2026-10-01T00:00:00-05:00", 5)]
    [InlineData("2026-10-01T00:00:00", 0)]
    [InlineData("2026-10-01", 0)]
    public void DateTime_FromJsonString_IsUtcAtTheSameInstant(string value, int expectedUtcHour)
    {
        var constant = JsonConstantOf($$"""{"rules":[{"field":"CreatedAt","operator":"GreaterThanOrEqual","value":"{{value}}"}]}""");

        var dt = Assert.IsType<DateTime>(constant);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, expectedUtcHour, 0, 0, DateTimeKind.Utc).Ticks, dt.Ticks);
    }

    [Theory]
    [InlineData("2026-10-01T00:00:00Z", 0)]
    [InlineData("2026-10-01T00:00:00-05:00", 5)]
    [InlineData("2026-10-01", 0)]
    public void DateTime_FromClrString_IsUtcAtTheSameInstant(string value, int expectedUtcHour)
    {
        var constant = ConstantOf(new FilterRule("CreatedAt", FilterOperator.GreaterThanOrEqual, value));

        var dt = Assert.IsType<DateTime>(constant);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, expectedUtcHour, 0, 0, DateTimeKind.Utc).Ticks, dt.Ticks);
    }

    [Fact]
    public void DateTime_ClrLocalValue_IsConvertedToUtcAtTheSameInstant()
    {
        var local = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Local);

        var dt = Assert.IsType<DateTime>(ConstantOf(new FilterRule("CreatedAt", FilterOperator.LessThan, local)));

        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(local.ToUniversalTime().Ticks, dt.Ticks);
    }

    [Fact]
    public void DateTime_ClrUnspecifiedValue_IsReadAsUtc()
    {
        var unspecified = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Unspecified);

        var dt = Assert.IsType<DateTime>(ConstantOf(new FilterRule("CreatedAt", FilterOperator.LessThan, unspecified)));

        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(unspecified.Ticks, dt.Ticks);
    }

    [Theory]
    [InlineData("2026-10-01T00:00:00-05:00", 5)]
    [InlineData("2026-10-01T00:00:00Z", 0)]
    [InlineData("2026-10-01", 0)]
    public void DateTimeOffset_FromJsonString_HasZeroOffset(string value, int expectedUtcHour)
    {
        var constant = JsonConstantOf($$"""{"rules":[{"field":"ClosedAt","operator":"LessThan","value":"{{value}}"}]}""");

        var dto = Assert.IsType<DateTimeOffset>(constant);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
        Assert.Equal(new DateTime(2026, 10, 1, expectedUtcHour, 0, 0, DateTimeKind.Utc), dto.UtcDateTime);
    }

    [Fact]
    public void DateTimeOffset_ClrValueWithOffset_HasZeroOffset()
    {
        var value = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-5));

        var dto = Assert.IsType<DateTimeOffset>(ConstantOf(new FilterRule("ClosedAt", FilterOperator.LessThan, value)));

        Assert.Equal(TimeSpan.Zero, dto.Offset);
        Assert.Equal(value.UtcDateTime, dto.UtcDateTime);
    }

    [Fact]
    public void In_WithJsonDateStrings_YieldsUtcElements()
    {
        var group = ParseJson("""{"rules":[{"field":"CreatedAt","operator":"In","value":["2026-10-01T00:00:00Z","2026-10-02"]}]}""");

        var body = FilterExpressionBuilder.Build<Row>(group).Body;
        var call = Assert.IsAssignableFrom<MethodCallExpression>(body);
        var array = Assert.IsType<DateTime[]>(Assert.IsAssignableFrom<ConstantExpression>(call.Arguments[0]).Value);

        Assert.All(array, d => Assert.Equal(DateTimeKind.Utc, d.Kind));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).Ticks, array[0].Ticks);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc).Ticks, array[1].Ticks);
    }

    #endregion
}
