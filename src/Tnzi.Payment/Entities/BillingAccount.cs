namespace Tnzi.Payment.Entities;

/// <summary>
/// 计费账户的账单资料：发票抬头与账单联系人。
/// </summary>
/// <remarks>
/// <para>
/// 账户本身不需要这一行就存在（它只是 <see cref="BillingAccountRef"/> 两列）：没有资料时，开票与催款沿用经办人自己的
/// 名字与邮箱，与引入计费账户之前相同。资料存在的理由是共同管账：单客户部署里账户不属于任何一个人，
/// 「账单寄给谁」不能取决于这个月是哪位管理者点的付款，更不能随那位管理者离职而失效。
/// 所以「转让所有权」不是一个单独的动作：没有哪个用户是所有者，换联系人就是转让。
/// </para>
/// <para>每个账户至多一行（过滤唯一索引）。</para>
/// </remarks>
public class BillingAccount : MultiTenantAuditedEntity<Guid>
{
    /// <summary><see cref="BillingEmails"/> 列宽。</summary>
    public const int BillingEmailsMaxLength = 1000;

    /// <summary>账户种类（见 <see cref="BillingAccountTypes"/>）。</summary>
    public string AccountType { get; set; } = string.Empty;

    /// <summary>账户 Id。</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>账单上的客户名称（机构全称）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 账单联系邮箱，可以多个，以分号分隔。催款、续费提醒、发票都发给这里的每一个地址。
    /// </summary>
    public string? BillingEmails { get; set; }

    /// <summary>账单地址（多行文本）。</summary>
    public string? Address { get; set; }

    /// <summary>客户税号（例如 HST / GST 登记号），印在发票的买方一栏。</summary>
    public string? TaxId { get; set; }

    /// <summary>拆开 <see cref="BillingEmails"/>：去空白、去重、保持顺序。</summary>
    public IReadOnlyList<string> GetBillingEmails()
        => SplitEmails(BillingEmails);

    /// <summary>把以分号或逗号分隔的邮箱串拆成列表（去空白、不分大小写去重、保持顺序）。</summary>
    public static IReadOnlyList<string> SplitEmails(string? emails)
        => string.IsNullOrWhiteSpace(emails)
            ? []
            : emails.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
