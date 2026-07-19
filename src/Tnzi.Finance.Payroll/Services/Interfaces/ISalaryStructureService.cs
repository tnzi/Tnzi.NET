namespace Tnzi.Finance.Payroll.Services.Interfaces;

/// <summary>
/// 薪资结构服务（行内嵌全量重建；保存时做依赖序 + 未知变量静态校验）
/// </summary>
public interface ISalaryStructureService
{
    /// <summary>分页查询结构（不含行）</summary>
    Task<Result<IPagedList<SalaryStructureListDto>>> GetPagedAsync(SalaryStructureQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取结构（含行，按序号升序）</summary>
    Task<Result<SalaryStructureDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建结构</summary>
    Task<Result<SalaryStructureDto>> CreateAsync(CreateSalaryStructureDto input, CancellationToken cancellationToken = default);

    /// <summary>更新结构（行硬删全量重建）</summary>
    Task<Result<SalaryStructureDto>> UpdateAsync(Guid id, UpdateSalaryStructureDto input, CancellationToken cancellationToken = default);

    /// <summary>删除结构（软删除；被薪资分配引用时拒绝）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
