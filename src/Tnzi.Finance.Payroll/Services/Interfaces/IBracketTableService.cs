namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 税级表服务（行内嵌全量重建 + 连续性校验；同编码多版本按生效日解析）
/// </summary>
public interface IBracketTableService
{
    /// <summary>分页查询税级表（不含行）</summary>
    Task<Result<IPagedList<BracketTableListDto>>> GetPagedAsync(BracketTableQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取税级表（含行，按序号升序）</summary>
    Task<Result<BracketTableDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建税级表</summary>
    Task<Result<BracketTableDto>> CreateAsync(CreateBracketTableDto input, CancellationToken cancellationToken = default);

    /// <summary>更新税级表（行硬删全量重建）</summary>
    Task<Result<BracketTableDto>> UpdateAsync(Guid id, UpdateBracketTableDto input, CancellationToken cancellationToken = default);

    /// <summary>删除税级表（软删除，行级联硬删）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析某日期生效的表版本：启用版本中 EffectiveFrom ≤ asOf 的最大者（含行）。
    /// 无命中返回 404 失败 Result
    /// </summary>
    Task<Result<BracketTableDto>> ResolveAsync(string code, DateTime asOf, CancellationToken cancellationToken = default);
}
