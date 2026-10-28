namespace Tnzi.Payment.Dtos;

/// <summary>
/// 当前用户所代表的计费账户
/// </summary>
public class BillingAccountDto
{
    /// <summary>账户种类（User / Installation / Tenant / 消费方自定义）</summary>
    public string AccountType { get; set; } = string.Empty;

    /// <summary>账户 Id</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>当前用户能否管理这个账户（付款、订阅、卡片、资料）。前端据此决定显示补缴 / 换卡入口还是「请联系管理员」。</summary>
    public bool CanManage { get; set; }

    /// <summary>账单上的客户名称；未填写资料时为 null</summary>
    public string? DisplayName { get; set; }

    /// <summary>账单联系邮箱</summary>
    public List<string> BillingEmails { get; set; } = [];

    /// <summary>账单地址</summary>
    public string? Address { get; set; }

    /// <summary>客户税号</summary>
    public string? TaxId { get; set; }
}

/// <summary>
/// 更新当前计费账户的账单资料
/// </summary>
public class UpdateBillingAccountDto
{
    /// <summary>账单上的客户名称</summary>
    [MaxLength(256, ErrorMessage = "DisplayName cannot exceed 256 characters.")]
    public string? DisplayName { get; set; }

    /// <summary>账单联系邮箱（每一个都必须是合法地址）</summary>
    public List<string>? BillingEmails { get; set; }

    /// <summary>账单地址</summary>
    [MaxLength(1000, ErrorMessage = "Address cannot exceed 1000 characters.")]
    public string? Address { get; set; }

    /// <summary>客户税号</summary>
    [MaxLength(64, ErrorMessage = "TaxId cannot exceed 64 characters.")]
    public string? TaxId { get; set; }
}

/// <summary>
/// 账单联系人：开票与催款要用的名字与收件地址。
/// </summary>
/// <param name="Name">客户名称</param>
/// <param name="Emails">收件地址（至少一个时才发得出邮件）</param>
/// <param name="Company">公司名称（只有消费应用的 <c>IBillingAccountProfileProvider</c> 给得出）</param>
/// <param name="Address">账单地址</param>
/// <param name="TaxId">客户税号</param>
public sealed record BillingContact(string? Name, IReadOnlyList<string> Emails, string? Company = null, string? Address = null, string? TaxId = null)
{
    /// <summary>第一个收件地址（快照列只存一个）</summary>
    public string? PrimaryEmail => Emails.Count > 0 ? Emails[0] : null;
}
