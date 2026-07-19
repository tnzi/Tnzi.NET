namespace Tnzi.Application;

/// <summary>
/// 泛型 CRUD 应用服务契约。由 <see cref="CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/> 实现，
/// 供泛型 admin 控制器（<c>Tnzi.AspNetCore.Mvc.CrudAdminControllerBase</c>）以最少样板转发。
/// </summary>
/// <typeparam name="TKey">主键类型</typeparam>
/// <typeparam name="TDto">读取/返回 DTO</typeparam>
/// <typeparam name="TCreateDto">创建输入 DTO</typeparam>
/// <typeparam name="TUpdateDto">更新输入 DTO</typeparam>
[ExperimentalApi(Reason = "Generic CRUD application-service contract is an opt-in convenience surface still stabilising")]
public interface ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto>
    where TKey : notnull
{
    /// <summary>分页查询。</summary>
    Task<Result<IPagedList<TDto>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>按主键取详情（范围外/不存在返回 404）。</summary>
    Task<Result<TDto>> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>新建。</summary>
    Task<Result<TDto>> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);

    /// <summary>更新（范围外/不存在返回 404）。</summary>
    Task<Result<TDto>> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);

    /// <summary>按主键删除（范围外/不存在返回 404）。</summary>
    Task<Result> DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>批量删除（范围外的 id 静默跳过）。</summary>
    Task<Result> BatchDeleteAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);
}
