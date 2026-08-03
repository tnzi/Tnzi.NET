namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资组件服务（保存期做语法校验 + 自引用拒绝；依赖序校验在结构保存时进行）
/// </summary>
public interface ISalaryComponentService
{
    /// <summary>分页查询组件</summary>
    Task<Result<IPagedList<SalaryComponentDto>>> GetPagedAsync(SalaryComponentQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取组件</summary>
    Task<Result<SalaryComponentDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建组件</summary>
    Task<Result<SalaryComponentDto>> CreateAsync(CreateSalaryComponentDto input, CancellationToken cancellationToken = default);

    /// <summary>更新组件</summary>
    Task<Result<SalaryComponentDto>> UpdateAsync(Guid id, UpdateSalaryComponentDto input, CancellationToken cancellationToken = default);

    /// <summary>删除组件（软删除；被结构行引用时拒绝）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按编码查找（内部/country pack 播种与外部摄取用）</summary>
    Task<SalaryComponent?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
