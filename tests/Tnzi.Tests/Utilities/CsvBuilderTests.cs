using System.Globalization;

namespace Tnzi.Tests.Utilities;

/// <summary>
/// CsvBuilder 单元测试:公式注入防护、RFC 4180 引号转义、invariant culture 类型化格式输出
/// </summary>
public class CsvBuilderTests
{
    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1234", "'+1234")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@import", "'@import")]
    [InlineData("\tvalue", "'\tvalue")]
    public void EscapeCell_FormulaInjection_PrefixesApostrophe(string input, string expected)
    {
        Assert.Equal(expected, CsvBuilder.EscapeCell(input));
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\rline2", "\"line1\rline2\"")]
    public void EscapeCell_Rfc4180_QuotesSpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, CsvBuilder.EscapeCell(input));
    }

    [Fact]
    public void EscapeCell_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CsvBuilder.EscapeCell(null));
        Assert.Equal(string.Empty, CsvBuilder.EscapeCell(string.Empty));
    }

    [Fact]
    public void EscapeCell_FormulaWithComma_AppliesBothProtections()
    {
        // 公式注入防护先行,引号包裹在外
        Assert.Equal("\"'=CMD|' /C calc'!A0,x\"", CsvBuilder.EscapeCell("=CMD|' /C calc'!A0,x"));
    }

    [Fact]
    public void AppendRow_MixedTypes_FormatsInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // 德语区域小数点是逗号,invariant 输出必须不受影响
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var csv = new CsvBuilder("yyyy-MM-dd");
            csv.AppendRow("name", 1234.56m, new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc), null, true);

            Assert.Equal("name,1234.56,2026-07-13,,True", csv.ToString().TrimEnd('\r', '\n'));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AppendRow_DefaultDateFormat_IsRoundTrip()
    {
        var csv = new CsvBuilder();
        var time = new DateTime(2026, 7, 13, 8, 30, 15, DateTimeKind.Utc);
        csv.AppendRow(time);

        Assert.Equal(time.ToString("o", CultureInfo.InvariantCulture), csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AppendRow_NullableBoxedValues_EmitEmptyCells()
    {
        DateTime? noDate = null;
        int? noNumber = null;
        var csv = new CsvBuilder();
        csv.AppendRow("a", noDate, noNumber, "b");

        Assert.Equal("a,,,b", csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AppendRow_StringCells_GetFormulaProtection()
    {
        var csv = new CsvBuilder();
        csv.AppendRow("=danger", "safe");

        Assert.Equal("'=danger,safe", csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AppendRow_NegativeDecimal_NotEscaped()
    {
        // 数值走类型化分支,不经公式转义(负数不得被加引号前缀)
        var csv = new CsvBuilder();
        csv.AppendRow(-15.5m);

        Assert.Equal("-15.5", csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AppendRow_NegativeIntegral_NotEscaped()
    {
        // 整型/浮点数值走类型化分支,不经公式转义(负数以 '-' 开头不得被误加引号前缀变成文本)
        var csv = new CsvBuilder();
        csv.AppendRow(-42, -7L, -3.5d);

        Assert.Equal("-42,-7,-3.5", csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AppendRow_MultipleRows_ProducesMultipleLines()
    {
        var csv = new CsvBuilder();
        csv.AppendRow("h1", "h2").AppendRow("v1", "v2");

        var lines = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(["h1,h2", "v1,v2"], lines);
    }

    [Fact]
    public void AppendRow_Guid_And_Enum_UseInvariantConvertToString()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var csv = new CsvBuilder();
        csv.AppendRow(id, DayOfWeek.Monday, 42);

        Assert.Equal($"{id},Monday,42", csv.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Constructor_BlankDateFormat_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CsvBuilder(" "));
    }
}
