namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行规则管理（CRUD + 排序 + 试跑）
/// </summary>
public interface IBankRuleService
{
    /// <summary>分页查询（按优先级升序）</summary>
    Task<Result<IPagedList<BankRuleDto>>> GetPagedAsync(BankRuleQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取规则（含条件）</summary>
    Task<Result<BankRuleDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建规则</summary>
    Task<Result<BankRuleDto>> CreateAsync(CreateBankRuleDto input, CancellationToken cancellationToken = default);

    /// <summary>更新规则（条件全量替换）</summary>
    Task<Result<BankRuleDto>> UpdateAsync(Guid id, CreateBankRuleDto input, CancellationToken cancellationToken = default);

    /// <summary>删除规则</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按给定顺序重排优先级</summary>
    Task<Result> ReorderAsync(ReorderBankRulesDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 试跑：这条规则会命中哪些待匹配流水，以及每条流水最终归谁。
    /// </summary>
    Task<Result<BankRuleTestResultDto>> TestAsync(Guid id, TestBankRuleDto input, CancellationToken cancellationToken = default);
}
