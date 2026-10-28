namespace Tnzi.Storage.Workspace.Tests.TestSupport;

/// <summary>
/// 不设配额的配额服务：不关心配额的用例拿它构造写路径的服务，一条查询都不发。
/// 关心配额的用例构造真实的 <see cref="StorageQuotaService"/>。
/// </summary>
public sealed class UnlimitedStorageQuota : IStorageQuotaService
{
    public static readonly UnlimitedStorageQuota Instance = new();

    public Task<StorageQuotaDto> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new StorageQuotaDto { UserId = userId });

    public Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(0L);

    public Task<Tnzi.Results.Result<T>?> EnsureCapacityAsync<T>(Guid? ownerId, long additionalBytes, CancellationToken cancellationToken = default)
        => Task.FromResult<Tnzi.Results.Result<T>?>(null);
}
