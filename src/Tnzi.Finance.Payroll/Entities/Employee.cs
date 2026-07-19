namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 员工（薪酬主数据）
/// </summary>
/// <remarks>
/// 与财务往来方的衔接采用"影子 Vendor"：<see cref="VendorId"/> 由
/// <c>IEmployeeService.EnsurePayeeVendorAsync</c> 幂等创建并回填，报销/预支等
/// 真 A/P 流走该 Vendor（payee doctrine），薪酬净额本身不进 A/P 结算链。
/// </remarks>
public class Employee : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 员工编码（必填，租户内唯一）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 姓名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 电话
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// 入职日期（date-only）
    /// </summary>
    public DateTime? HireDate { get; set; }

    /// <summary>
    /// 离职日期（date-only；null 表示在职）
    /// </summary>
    public DateTime? TerminationDate { get; set; }

    /// <summary>
    /// 影子供应商（松引用；报销等真 A/P 流的 payee）
    /// </summary>
    public Guid? VendorId { get; set; }

    /// <summary>
    /// 关联系统用户（松引用，不校验存在性；员工自助等场景使用）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 扩展属性（JSON 对象，标量值；公式经 Attr()/AttrText() 读取，
    /// country pack 的报税身份/免税额等经此通道传递）
    /// </summary>
    public string? AttributesJson { get; set; }

    /// <summary>
    /// 是否在册（false 不参与发薪圈选）
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 备注
    /// </summary>
    public string? Notes { get; set; }
}
