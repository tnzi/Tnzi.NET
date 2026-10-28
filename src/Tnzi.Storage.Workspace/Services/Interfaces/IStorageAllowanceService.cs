namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 管理员按用户覆盖存储配额（<see cref="StorageAllowance"/>）。用户的实际用量与生效配额由父模块的
/// <c>IStorageQuotaService</c> 给（<c>GET admin/files/quota/{userId}</c>）。
/// </summary>
/// <remarks>
/// 覆盖值只有在本模块的 <see cref="StorageAllowanceProvider"/> 是配额来源时才生效。消费方注册了自己的
/// <c>IStorageAllowanceProvider</c>（按套餐、按角色）时，这里写下的值没有人读。
/// </remarks>
public interface IStorageAllowanceService
{
    /// <summary>有覆盖值的用户，按最近修改在前。</summary>
    Task<Result<IPagedList<StorageAllowanceDto>>> GetListAsync(StorageAllowanceQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>一个用户的覆盖值；没有覆盖值答 404（他用默认值）。</summary>
    Task<Result<StorageAllowanceDto>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>设置一个用户的覆盖值（有就改，没有就建）。</summary>
    Task<Result<StorageAllowanceDto>> SetAsync(Guid userId, SetStorageAllowanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>去掉一个用户的覆盖值，他回到默认值。本来就没有也算成功。</summary>
    Task<Result> RemoveAsync(Guid userId, CancellationToken cancellationToken = default);
}
