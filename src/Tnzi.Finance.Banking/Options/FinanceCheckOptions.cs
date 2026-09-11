namespace Tnzi.Finance.Banking.Options;

/// <summary>
/// 支票票面的呈现配置（银行域拥有，非会计内核）
/// </summary>
/// <remarks>
/// <b>配置节仍是 <c>Finance</c>，键路径 <c>Finance:CheckNumberDigits</c></b>，分组键复用
/// <c>finance-general</c>、小节仍是 <c>Checks</c> —— 与核心 <see cref="Tnzi.Finance.Options.FinanceOptions"/>
/// 里既有的 <c>Check*</c> 设置项并排出现在配置中心的同一块屏上，运维看不出它换了承载类型。
/// <para>
/// ★ <b>为什么不加进 <c>FinanceOptions</c></b>：核心的 <c>CLAUDE.md</c> 已经把「9 个只服务于
/// 银行域 / 文档子模块的 <c>[RuntimeSetting]</c> 留在核心」登记为待修项 —— 不加载这两个子模块的
/// 宿主会渲染出一组控制不了任何东西的设置项。往那堆里再加第十个，是把一个已知问题做大。
/// 本类沿 <c>FinanceOfferOptions</c> 的做法：同节、同组、同小节，但**归属正确的模块**。
/// </para>
/// </remarks>
[ConfigSection("Finance")]
[RuntimeSettingGroup(Key = "finance-general", Module = "Finance", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.financeGeneral",
    Icon = "mdi:receipt-text-outline", Order = 550)]
public class FinanceCheckOptions
{
    /// <summary>支票号在票面与 MICR 串行号上补零到的位数（下限，不截断）。</summary>
    /// <remarks>
    /// 银行要求别的串行号长度时改这里。<b>1 = 不补零</b>。规则本身在
    /// <see cref="Services.CheckNumberFormat"/>，消费应用可以调它把自己界面上的支票号
    /// 渲染成与纸上逐字相同的样子。
    /// </remarks>
    [RuntimeSetting(Label = "Cheque Number Digits", I18n = "admin.modules.system.settings.fields.checkNumberDigits",
        Type = SettingFieldType.Int, Required = false, Subsection = "Checks",
        Description = "Zero-pad the cheque number to this many digits on the cheque face and in the MICR serial field. "
            + "1 means no padding. Longer numbers are printed in full, never truncated.")]
    public int CheckNumberDigits { get; set; } = CheckNumberFormat.DefaultDigits;
}
