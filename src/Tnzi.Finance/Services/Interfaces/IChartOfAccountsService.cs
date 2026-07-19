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

    /// <summary>更新科目（RootType 不可变；停用挂着系统角色的科目会被拒绝）</summary>
    Task<Result<AccountDto>> UpdateAsync(Guid id, UpdateAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>删除科目（挂着系统角色、存在子科目或分录时拒绝）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量读取科目余额（本位币口径，截至 <paramref name="asOf"/> 日终，仅统计已过账行）
    /// </summary>
    /// <remarks>
    /// 与资产负债表/试算平衡共用同一聚合读路径，故 as-of 边界（<c>PostingDate &lt; 次日</c>）
    /// 与报表恒等——未来日期的过账不进当日余额；<c>Finance:Reports:UseBalanceSummary</c>
    /// 快路径一并继承。分组科目恒 0（分录只落叶子）；币种限定科目回本位币折算额，
    /// 交易币余额不在此口径内。无分录/未知科目回 0 行，结果与入参一一对应。
    /// </remarks>
    /// <param name="accountIds">科目ID集合（去重后上限 500——参数化 IN 列表有数据库上限，
    /// 超过请分批）</param>
    /// <param name="asOf">基准日（含当日）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<List<AccountBalanceDto>>> GetBalancesAsync(IEnumerable<Guid> accountIds, DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>为当前租户播种默认科目表（仅当科目表为空时）</summary>
    Task<Result<int>> SeedDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>按系统角色解析科目（内部/跨模块调用）</summary>
    Task<Account?> FindByRoleAsync(AccountSystemRole role, CancellationToken cancellationToken = default);

    /// <summary>按编码解析科目（内部/跨模块调用）</summary>
    Task<Account?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
