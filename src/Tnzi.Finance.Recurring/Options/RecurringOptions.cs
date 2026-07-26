namespace Tnzi.Finance.Recurring.Options;

/// <summary>
/// 周期性单据配置
/// </summary>
[ConfigSection("Finance:Recurring")]
// PermissionGroup 显式指向 finance：本子模块没有与自身同名的权限组，缺省会从模块名
// 派生出 "financerecurring" 这个并不存在的组，配置权限随即被播种器整条丢弃。
[RuntimeSettingGroup(Key = "finance-recurring", Module = "Finance", DisplayName = "Recurring",
    Icon = "mdi:calendar-sync-outline", Order = 60, PermissionGroup = "finance",
    I18nKey = "admin.modules.system.settings.groups.financeRecurring")]
public class RecurringOptions
{
    /// <summary>
    /// 作业停机之后的补齐语义。
    /// </summary>
    /// <remarks>
    /// 默认 <see cref="RecurringCatchUpPolicy.GenerateAll"/>：不补等于凭空少掉真实
    /// 发生的收入；多出来的草稿至少看得见、删得掉，少掉的那张没人会发现。
    /// </remarks>
    [RuntimeSetting(Label = "Recurring: Catch-up Policy", I18n = "admin.modules.system.settings.fields.recurringCatchUpPolicy",
        Type = SettingFieldType.Select, Subsection = "Recurring",
        Options = "GenerateAll,LatestOnly,Skip",
        Description = "What to do when the generator has been down and periods were missed.")]
    public RecurringCatchUpPolicy CatchUpPolicy { get; set; } = RecurringCatchUpPolicy.GenerateAll;

    /// <summary>
    /// 生成的单据是否直接过账。
    /// </summary>
    /// <remarks>
    /// 默认 false：自动生成**草稿**，过账仍是人的决定。让日历直接往总账里写东西，
    /// 是最容易在月底才被发现的那种错。
    /// </remarks>
    [RuntimeSetting(Label = "Recurring: Auto-post Generated Documents", I18n = "admin.modules.system.settings.fields.recurringDefaultAutoPost",
        Type = SettingFieldType.Boolean, Subsection = "Recurring",
        Description = "Off by default: the generator creates drafts and posting stays a human decision.")]
    public bool DefaultAutoPost { get; set; }

    /// <summary>单次运行最多补几期（防止一个配错的锚点日期生成上千张单据）</summary>
    public int MaxCatchUpPerRun { get; set; } = 24;

    /// <summary>
    /// 同一期次最多尝试几次（含首次）；&lt;= 1 表示失败即不再重试。
    /// </summary>
    /// <remarks>
    /// ★这是"失败留痕**可重试**"里"可重试"三个字的落实处。排期是**无条件**往前推的
    /// （否则一条永远失败的模板会在每次扫描里卡住同一期），所以失败的那一期此后
    /// 再也不会被日历扫到 —— 必须由这里主动把它捡回来，否则停用的科目启用回来之后，
    /// 那张发票永远补不上，而没有人会发现。
    ///
    /// 有上限是因为一条永远失败的模板会每轮多写一行失败记录；三次之后基本可以断定
    /// 不是暂时性故障，记录留在表里等人处理即可。
    /// </remarks>
    public int MaxFailedRetries { get; set; } = 3;

    /// <summary>
    /// 进程内扫描间隔（分钟）；&lt;= 0 关闭，改由外部调度打 <c>run-due</c> 端点。
    /// </summary>
    /// <remarks>
    /// 纯启动配置而非 <c>[RuntimeSetting]</c>：后台循环在启动时读一次并据此建
    /// 定时器，把它做成可热改只会让界面上显示的值与实际节奏对不上。
    /// </remarks>
    public int SweepIntervalMinutes { get; set; } = 60;
}
