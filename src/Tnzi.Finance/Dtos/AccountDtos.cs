namespace Tnzi.Finance.Dtos;

/// <summary>
/// 会计科目 DTO
/// </summary>
public class AccountDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AccountRootType RootType { get; set; }
    public string? SubType { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string? Currency { get; set; }
    public AccountSystemRole? SystemRole { get; set; }
    public CashFlowActivity? CashFlowActivity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 会计科目树节点 DTO
/// </summary>
public class AccountTreeDto : AccountDto
{
    public List<AccountTreeDto> Children { get; set; } = new();
}

/// <summary>
/// 创建会计科目请求
/// </summary>
public class CreateAccountDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public AccountRootType RootType { get; set; }
    public string? SubType { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string? Currency { get; set; }
    public AccountSystemRole? SystemRole { get; set; }
    public CashFlowActivity? CashFlowActivity { get; set; }
}

/// <summary>
/// 更新会计科目请求（全量更新；RootType 创建后不可变）
/// </summary>
public class UpdateAccountDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? SubType { get; set; }
    public Guid? ParentId { get; set; }
    public string? Currency { get; set; }
    public AccountSystemRole? SystemRole { get; set; }
    public CashFlowActivity? CashFlowActivity { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 会计科目查询请求
/// </summary>
public class AccountQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按根类型过滤</summary>
    public AccountRootType? RootType { get; set; }

    /// <summary>是否仅启用科目</summary>
    public bool? IsActive { get; set; }
}
