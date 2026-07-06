namespace Tnzi.Finance.Dtos;

/// <summary>
/// 目录项 DTO
/// </summary>
public class ItemDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public string? Description { get; set; }
    public decimal? SalesPrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public Guid? IncomeAccountId { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建目录项请求
/// </summary>
public class CreateItemDto
{
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public ItemType Type { get; set; } = ItemType.Service;
    public string? Description { get; set; }
    public decimal? SalesPrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public Guid? IncomeAccountId { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? DefaultTaxCodeId { get; set; }
}

/// <summary>
/// 更新目录项请求（全量更新）
/// </summary>
public class UpdateItemDto : CreateItemDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 目录项查询请求
/// </summary>
public class ItemQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按类型过滤</summary>
    public ItemType? Type { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
