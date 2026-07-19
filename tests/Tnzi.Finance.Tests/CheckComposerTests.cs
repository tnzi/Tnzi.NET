using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests;

/// <summary>
/// P3 块 2：MICR 行拼装 + 支票金额英文大写（纯函数）
/// </summary>
public class CheckComposerTests
{
    private static readonly char T = MicrLineComposer.Transit;
    private static readonly char U = MicrLineComposer.OnUs;
    private static readonly char D = MicrLineComposer.Dash;

    [Fact]
    public void Micr_UsAba_Composes()
    {
        var line = MicrLineComposer.Compose(BankNumberScheme.UsAba, 1001, "021000021", null, null, "123456789");
        line.ShouldBe($"{U}1001{U} {T}021000021{T} 123456789{U}");

        // 映射到 E-13B 字体码位后不再含 OCR 符号
        var glyphs = MicrLineComposer.ToFontGlyphs(line);
        glyphs.ShouldContain("A"); // Transit → A
        glyphs.ShouldContain("C"); // On-Us → C
        glyphs.ShouldNotContain(T.ToString());
        glyphs.ShouldNotContain(U.ToString());
    }

    [Fact]
    public void Micr_CaCpa006_Composes()
    {
        var line = MicrLineComposer.Compose(BankNumberScheme.CaEft, 1001, null, "001", "12345", "987654321");
        line.ShouldBe($"{U}1001{U} {T}12345{D}001{T} 987654321{U}");
    }

    [Theory]
    [InlineData(0.05, "USD", "Zero and 05/100 Dollars")]
    [InlineData(1.00, "USD", "One and 00/100 Dollars")]
    [InlineData(1234.56, "USD", "One Thousand Two Hundred Thirty-Four and 56/100 Dollars")]
    [InlineData(1000000, "USD", "One Million and 00/100 Dollars")]
    [InlineData(100, "CAD", "One Hundred and 00/100 Dollars")]
    [InlineData(50, "EUR", "Fifty and 00/100 EUR")]
    [InlineData(19, "USD", "Nineteen and 00/100 Dollars")]
    [InlineData(0, "USD", "Zero and 00/100 Dollars")]
    public void AmountInWords_Boundaries(double amount, string currency, string expected)
    {
        CheckAmountInWords.Convert((decimal)amount, currency).ShouldBe(expected);
    }
}
