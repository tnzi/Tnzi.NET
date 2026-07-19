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
/// 批量读取科目余额请求
/// </summary>
public class GetAccountBalancesDto
{
    /// <summary>科目ID集合（去重后上限 500，超过请分批）</summary>
    public List<Guid> AccountIds { get; set; } = null!;

    /// <summary>基准日（含当日；不传取今日 UTC）</summary>
    public DateTime? AsOf { get; set; }
}

/// <summary>
/// 科目余额（本位币口径，截至 <see cref="AsOf"/> 日终）
/// </summary>
public class AccountBalanceDto
{
    public Guid AccountId { get; set; }

    /// <summary>基准日（余额 = PostingDate 落在当日及以前的全部已过账行）</summary>
    public DateTime AsOf { get; set; }

    /// <summary>借方累计（本位币）</summary>
    public decimal Debit { get; set; }

    /// <summary>贷方累计（本位币）</summary>
    public decimal Credit { get; set; }

    /// <summary>
    /// 有符号余额（Debit − Credit，借方为正）。负债/权益/收入科目按此口径自然为负，
    /// 呈现端如需“正数显示”自行按 RootType 取反——此处不做正负归一化，与总账口径一致
    /// </summary>
    public decimal Balance { get; set; }
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
