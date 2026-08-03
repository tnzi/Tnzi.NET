namespace Tnzi.Finance.Services;

/// <summary>
/// 银行对账服务（join 表方案：勾选行引用已过账总账行，不修改总账）
/// </summary>
public interface IReconciliationService
{
    /// <summary>分页查询对账</summary>
    Task<Result<IPagedList<ReconciliationDto>>> GetPagedAsync(ReconciliationQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取对账（含累计已勾选净额与差额）</summary>
    Task<Result<ReconciliationDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建对账草稿（科目须为可过账资金叶子且限本位币；同一科目同时只允许一张 Draft）
    /// </summary>
    Task<Result<ReconciliationDto>> CreateDraftAsync(CreateReconciliationDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿头字段（对账单日期/期末余额/备注）</summary>
    Task<Result<ReconciliationDto>> UpdateDraftAsync(Guid id, CreateReconciliationDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿（勾选行级联硬删）</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 勾选工作区：该科目的已过账行（本对账已勾选 + 未被任何对账勾选的候选）+ 实时差额
    /// </summary>
    Task<Result<ReconciliationWorksheetDto>> GetWorksheetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>全量替换勾选行（仅 Draft；行须属于对账科目、已过账、未被其它对账占用）</summary>
    Task<Result<ReconciliationWorksheetDto>> SetLinesAsync(Guid id, SetReconciliationLinesDto input, CancellationToken cancellationToken = default);

    /// <summary>完成对账（差额须为 0；完成后锁定）</summary>
    Task<Result<ReconciliationDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default);
}
