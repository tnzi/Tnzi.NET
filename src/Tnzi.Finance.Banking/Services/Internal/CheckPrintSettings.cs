namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 一次渲染实际生效的打印设置（模板 / 版式 / 票纸 / 两个偏移）
/// </summary>
/// <remarks>
/// 这四组值原先只有一个来源 —— 当前的银行账户档案。把它们收进一个显式的值对象，是为了让
/// <b>「这一次渲染用的是哪一套设置」</b> 成为调用方必须回答的问题：
/// <list type="bullet">
/// <item>打印 / 预览 → 当前档案（可被请求上的模板名单次覆盖）；</item>
/// <item>重新渲染一张<b>已开支票</b> → 开票时刻的快照（<see cref="ForIssuedCheck"/>），
///       否则换过模板或调过偏移之后，画出来的不是当初真的寄出去的那张纸；</item>
/// <item>重打（作废原票 + 开新号）→ 当前档案，因为那是一张<b>新的</b>票据，
///       而重打的常见起因恰恰是「刚调完偏移，要一张对得准的纸」。</item>
/// </list>
/// 不可变：所有派生都返回新实例（<c>with</c>），没有就地改写。
/// </remarks>
public sealed record CheckPrintSettings(
    string? TemplateName,
    CheckLayout Layout,
    CheckStockType StockType,
    decimal OffsetXMm,
    decimal OffsetYMm)
{
    /// <summary>当前银行账户档案上的设置（打印 / 预览 / 重打 / 校准页的口径）。</summary>
    public static CheckPrintSettings FromBank(BankAccount bank)
    {
        Check.NotNull(bank);
        return new CheckPrintSettings(
            bank.CheckTemplateName, bank.CheckLayout, bank.CheckStockType, bank.OffsetXMm, bank.OffsetYMm);
    }

    /// <summary>
    /// 已开支票的设置：<b>逐字段</b>取开票时刻的快照，无快照的字段回退当前档案。
    /// </summary>
    /// <remarks>
    /// 逐字段而非整体回退：存量支票与手工登记的支票五列全空，于是整条退回当前档案 ——
    /// 与本机制引入前<b>逐字相同</b>，消费应用只加列、不必回填任何数据。
    /// </remarks>
    public static CheckPrintSettings ForIssuedCheck(BankCheck check, BankAccount bank)
    {
        Check.NotNull(check);
        Check.NotNull(bank);
        return new CheckPrintSettings(
            check.PrintTemplateName ?? bank.CheckTemplateName,
            check.PrintLayout ?? bank.CheckLayout,
            check.PrintStockType ?? bank.CheckStockType,
            check.PrintOffsetXMm ?? bank.OffsetXMm,
            check.PrintOffsetYMm ?? bank.OffsetYMm);
    }

    /// <summary>
    /// 单次覆盖模板名（打印 / 预览请求上的 <c>TemplateName</c>）。
    /// </summary>
    /// <remarks>
    /// 空白 = 不覆盖（跟随银行档案），而不是「清空成渲染器默认模板」——
    /// 一个没填的可选字段不该把账户上配好的版式换掉。
    /// </remarks>
    public CheckPrintSettings WithTemplateOverride(string? templateName)
        => string.IsNullOrWhiteSpace(templateName) ? this : this with { TemplateName = templateName.Trim() };

    /// <summary>把已解析的模板名钉回设置（写进支票快照前用，让 null 只表示「没有快照」）。</summary>
    public CheckPrintSettings WithResolvedTemplate(string? resolvedTemplateName)
        => string.IsNullOrWhiteSpace(resolvedTemplateName) ? this : this with { TemplateName = resolvedTemplateName };
}
