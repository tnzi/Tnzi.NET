namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 支票 MICR 行拼装（E-13B 磁墨字符识别行）
/// </summary>
/// <remarks>
/// 两个方案 profile：
/// <list type="bullet">
/// <item>US ABA：<c>⑈{checkNo}⑈ ⑆{routing}⑆ {account}⑈</c></item>
/// <item>CA CPA-006：<c>⑈{checkNo}⑈ ⑆{transit}⑉{institution}⑆ {account}⑈</c></item>
/// </list>
/// 使用 Unicode OCR 符号（⑆ Transit / ⑈ On-Us / ⑉ Dash）便于测试断言与人工核对；
/// 渲染到白纸时经 <see cref="ToFontGlyphs"/> 映射到常见 E-13B TTF 的四个码位（A/B/C/D）。
/// 预印票纸（<see cref="Metadata.CheckStockType.PrePrinted"/>）不打 MICR，此拼装仅白纸全打印用。
/// </remarks>
internal static class MicrLineComposer
{
    /// <summary>Transit 符号（U+2446 ⑆）</summary>
    public const char Transit = '⑆';

    /// <summary>Amount 符号（U+2447 ⑇，本 profile 不用，保留完整四符号映射）</summary>
    public const char Amount = '⑇';

    /// <summary>On-Us 符号（U+2448 ⑈）</summary>
    public const char OnUs = '⑈';

    /// <summary>Dash 符号（U+2449 ⑉）</summary>
    public const char Dash = '⑉';

    /// <summary>
    /// 拼装 MICR 行（返回带 Unicode OCR 符号的字符串）。
    /// </summary>
    public static string Compose(BankNumberScheme scheme, long checkNumber, string? routingNumber, string? institutionNumber, string? transitNumber, string accountNumber)
    {
        Check.NotNullOrWhiteSpace(accountNumber);
        var account = accountNumber.Trim();

        return scheme switch
        {
            BankNumberScheme.UsAba =>
                $"{OnUs}{checkNumber}{OnUs} {Transit}{(routingNumber ?? string.Empty).Trim()}{Transit} {account}{OnUs}",
            BankNumberScheme.CaEft =>
                $"{OnUs}{checkNumber}{OnUs} {Transit}{(transitNumber ?? string.Empty).Trim()}{Dash}{(institutionNumber ?? string.Empty).Trim()}{Transit} {account}{OnUs}",
            _ => throw new BusinessException("Unknown bank number scheme for MICR encoding.")
        };
    }

    /// <summary>
    /// 把 Unicode OCR 符号映射到常见 E-13B TrueType 字体的码位（Transit→A / Amount→B / On-Us→C / Dash→D），
    /// 供白纸打印时用配置的 MICR 字体渲染。
    /// </summary>
    public static string ToFontGlyphs(string micrLine)
    {
        Check.NotNull(micrLine);
        return micrLine
            .Replace(Transit, 'A')
            .Replace(Amount, 'B')
            .Replace(OnUs, 'C')
            .Replace(Dash, 'D');
    }
}
