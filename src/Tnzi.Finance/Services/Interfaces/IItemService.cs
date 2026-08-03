namespace Tnzi.Finance.Services;

/// <summary>
/// 目录项服务
/// </summary>
public interface IItemService
{
    /// <summary>分页查询目录项</summary>
    Task<Result<IPagedList<ItemDto>>> GetPagedAsync(ItemQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取目录项</summary>
    Task<Result<ItemDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建目录项</summary>
    Task<Result<ItemDto>> CreateAsync(CreateItemDto input, CancellationToken cancellationToken = default);

    /// <summary>更新目录项</summary>
    Task<Result<ItemDto>> UpdateAsync(Guid id, UpdateItemDto input, CancellationToken cancellationToken = default);

    /// <summary>删除目录项（软删除）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
