namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 科目表服务
/// </summary>
public interface IChartOfAccountsService
{
    /// <summary>获取科目</summary>
    Task<Result<AccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>分页查询科目</summary>
    Task<Result<IPagedList<AccountDto>>> GetListAsync(AccountQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取完整科目树</summary>
    Task<Result<List<AccountTreeDto>>> GetTreeAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>创建科目</summary>
    Task<Result<AccountDto>> CreateAsync(CreateAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>更新科目（RootType 不可变）</summary>
    Task<Result<AccountDto>> UpdateAsync(Guid id, UpdateAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>删除科目（存在子科目或分录时拒绝）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>为当前租户播种默认科目表（仅当科目表为空时）</summary>
    Task<Result<int>> SeedDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>按系统角色解析科目（内部/跨模块调用）</summary>
    Task<Account?> FindByRoleAsync(AccountSystemRole role, CancellationToken cancellationToken = default);

    /// <summary>按编码解析科目（内部/跨模块调用）</summary>
    Task<Account?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
