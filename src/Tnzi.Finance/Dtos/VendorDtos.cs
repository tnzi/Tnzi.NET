namespace Tnzi.Finance.Dtos;

/// <summary>
/// 供应商 DTO
/// </summary>
public class VendorDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Currency { get; set; }
    public int? PaymentTermsDays { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建供应商请求
/// </summary>
public class CreateVendorDto
{
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Currency { get; set; }
    public int? PaymentTermsDays { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// 更新供应商请求（全量更新）
/// </summary>
public class UpdateVendorDto : CreateVendorDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 供应商查询请求
/// </summary>
public class VendorQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称/邮箱模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
