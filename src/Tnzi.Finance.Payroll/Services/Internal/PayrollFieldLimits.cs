namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 薪资主数据与发薪批次上由人（或外部薪资系统）填写的字符串字段的列宽，以及超长时的拒绝消息。
/// </summary>
/// <remarks>
/// <para>
/// ★ 测试库是 SQLite，不检查列宽：超长值在那里「保存成功」，在 SQL Server / PostgreSQL 上是一个不点名字段的截断 500。
/// 所以列宽在服务入口先查一遍，超长答 400 并点名字段；常量由实体配置直接引用，两处不可能漂移。
/// </para>
/// <para>
/// 人填的值<b>拒绝而不截断</b>（悄悄截掉的备注看起来合法，含义已经变了），与会计核心同一口径。
/// 系统自己拼出来的工资单错误消息走的是另一条路（<see cref="PayslipFieldLimits"/>：收敛、绝不失败）。
/// 公式与条件不在这里：它们的上限是 <c>PayrollOptions.FormulaMaxLength</c>（不超过列宽 4000），由求值器校验。
/// </para>
/// </remarks>
internal static class PayrollFieldLimits
{
    /// <summary>员工编号列宽</summary>
    internal const int EmployeeCodeMaxLength = 32;

    /// <summary>员工 / 组件 / 结构 / 税表名称列宽</summary>
    internal const int NameMaxLength = 200;

    /// <summary>员工邮箱列宽</summary>
    internal const int EmailMaxLength = 256;

    /// <summary>员工电话列宽</summary>
    internal const int PhoneMaxLength = 32;

    /// <summary>员工扩展属性（JSON）列宽</summary>
    internal const int AttributesJsonMaxLength = 4000;

    /// <summary>员工与薪资分配的备注列宽</summary>
    internal const int NotesMaxLength = 2000;

    /// <summary>组件 / 结构 / 税表说明列宽</summary>
    internal const int DescriptionMaxLength = 500;

    /// <summary>发薪批次摘要列宽</summary>
    internal const int MemoMaxLength = 500;

    /// <summary>外部薪资系统的批次号列宽</summary>
    internal const int ProviderRunIdMaxLength = 128;

    /// <summary>一次性输入的备注列宽</summary>
    internal const int InputNoteMaxLength = 500;

    /// <summary>工资单付款方式列宽</summary>
    internal const int PaymentMethodMaxLength = 32;

    /// <summary>
    /// 付款参考号的上限。参考号不单独存列，而是拼进付款凭证摘要（列宽 500），
    /// 摘要前缀约 45 字符，取会计核心收付款参考号的同一上限即可保证拼出来放得下。
    /// </summary>
    internal const int PaymentReferenceMaxLength = 128;

    /// <summary>
    /// 依次检查各字段，返回第一个超长字段的拒绝消息（点名字段与上限）；全部合规返回 <see langword="null"/>。
    /// </summary>
    internal static string? FirstTooLong(params (string? Value, int MaxLength, string Field)[] fields)
    {
        foreach (var (value, maxLength, field) in fields)
        {
            if (value != null && value.Length > maxLength)
                return $"The {field} must be at most {maxLength} characters.";
        }

        return null;
    }
}
