namespace Tnzi.Finance.Offers.Options;

/// <summary>
/// 报价单 / 采购订单的号段前缀配置。
/// </summary>
/// <remarks>
/// <b>配置节仍是 <c>Finance</c>，键路径一字不变</b>（<c>Finance:EstimateNumberPrefix</c> /
/// <c>Finance:PurchaseOrderNumberPrefix</c>）：拆的是承载它们的 C# 类型，不是运维手里的那份
/// appsettings.json。同一节绑到两个 Options 类是框架既有做法（核心的
/// <c>FinanceAccountingOptions</c> 就是同节的第二个类）。
///
/// <b>分组键复用 <c>finance-general</c></b>：<c>AttributeSettingDefinitionProvider</c> 按
/// 组 Key 合并各贡献者，所以这两个字段仍然落在配置中心「Finance / General」的 Numbering
/// 小节里，位置、标签与 i18n 键与拆分前逐字一致。**不加载本模块的宿主则根本看不到它们**——
/// 这正是要的：一个渲染出来却控制不了任何东西的设置项，比没有这个设置项更糟。
///
/// 无验证器：前缀是自由字符串，核心的 <c>FinanceOptionsValidator</c> 从未校验过它，
/// 拆出来也不新增一条校验（那会让既有配置在升级时突然启动失败）。
/// </remarks>
[ConfigSection("Finance")]
[RuntimeSettingGroup(Key = "finance-general", Module = "Finance", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.financeGeneral",
    Icon = "mdi:receipt-text-outline", Order = 550)]
public class FinanceOfferOptions
{
    /// <summary>报价单编号前缀</summary>
    [RuntimeSetting(Label = "Estimate Number Prefix", I18n = "admin.modules.system.settings.fields.estimateNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for estimate (quote) numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string EstimateNumberPrefix { get; set; } = "EST-";

    /// <summary>采购订单编号前缀</summary>
    [RuntimeSetting(Label = "Purchase Order Number Prefix", I18n = "admin.modules.system.settings.fields.purchaseOrderNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for purchase order numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string PurchaseOrderNumberPrefix { get; set; } = "PO-";
}
