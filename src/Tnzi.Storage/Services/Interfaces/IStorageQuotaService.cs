namespace Tnzi.Storage.Services;

/// <summary>
/// 按用户的存储配额：算用量、取配额、在写入之前把关。
/// </summary>
/// <remarks>
/// <para>
/// ★ 判定在服务层而不是控制器：把字节交给存储提供者的路径有十来条（直传、批量、MD5 直存、复制、压缩、解压，
/// 子模块里的分片完成与建新版本），控制器可以被消费方整体替换，服务层是唯一必经之处。每条写路径在交出字节
/// <b>之前</b>调 <see cref="EnsureCapacityAsync{T}"/>。
/// </para>
/// <para>
/// 没有配额（默认）时整条判定不查库：<see cref="IStorageAllowanceProvider"/> 先答「不限」，用量根本不算。
/// </para>
/// </remarks>
public interface IStorageQuotaService
{
    /// <summary>
    /// 一个用户的用量与配额。
    /// </summary>
    Task<StorageQuotaDto> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一个用户已用的字节数：名下文件记录的字节数，加上每个 <see cref="IStorageUsageContributor"/> 报的数。
    /// </summary>
    Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 再给 <paramref name="ownerId"/> 存 <paramref name="additionalBytes"/> 个字节会不会超出他的配额。
    /// 不会（或没有所有者 / 没有配额）返回 <c>null</c>；会就返回一个 413 / <c>FILE_QUOTA_EXCEEDED</c> 的失败结果。
    /// </summary>
    /// <param name="ownerId">将要持有这些字节的用户；<c>null</c>（后台任务、匿名请求）不受配额约束。</param>
    /// <param name="additionalBytes">这次写入新增的字节数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<T>?> EnsureCapacityAsync<T>(Guid? ownerId, long additionalBytes, CancellationToken cancellationToken = default);
}
