namespace Tnzi.Storage.Services;

/// <summary>
/// 让<b>别的程序集</b>把它为某个用户保留的字节算进配额用量（与 <see cref="IStorageCleanupContributor"/> 同一形状）。
/// </summary>
/// <remarks>
/// 父模块只认识自己的文件记录。工作区子模块在记录之外还留着文件的历史版本，那些字节同样占着存储：
/// 不算它们，一个用户可以靠反复建新版本绕过配额。以 <see cref="IEnumerable{T}"/> 注入，没有实现时是空集合。
/// </remarks>
public interface IStorageUsageContributor
{
    /// <summary>
    /// 这个用户在本程序集的表里占用的字节数（不含父模块文件记录自身的大小）。
    /// </summary>
    Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 这个范围在本程序集的表里占用的字节数（存储用量采样用，见核心的 <see cref="IStorageUsageReader"/>）。
    /// </summary>
    /// <remarks>
    /// 默认实现只答得出用户范围（转给 <see cref="GetUsedBytesAsync(Guid, CancellationToken)"/>），对整个安装与租户答 0：
    /// 在本程序集里另存了字节的贡献者应当覆盖它，否则那些字节不进按安装或按租户的计费用量。
    /// </remarks>
    Task<long> GetUsedBytesAsync(StorageUsageScope scope, CancellationToken cancellationToken = default)
        => scope is { Kind: StorageUsageScopeKind.User, Id: { } userId }
            ? GetUsedBytesAsync(userId, cancellationToken)
            : Task.FromResult(0L);
}
