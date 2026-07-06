namespace Tnzi.Finance.Dtos;

/// <summary>
/// 客户 DTO
/// </summary>
public class CustomerDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? Currency { get; set; }
    public int? PaymentTermsDays { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建客户请求
/// </summary>
public class CreateCustomerDto
{
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? Currency { get; set; }
    public int? PaymentTermsDays { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// 更新客户请求（全量更新）
/// </summary>
public class UpdateCustomerDto : CreateCustomerDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 客户查询请求
/// </summary>
public class CustomerQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称/邮箱模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
