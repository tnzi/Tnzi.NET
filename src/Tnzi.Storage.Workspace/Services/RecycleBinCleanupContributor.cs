namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 把「回收站里过期的条目」这一趟挂进父模块的后台清理（<c>Storage:Cleanup:Enabled</c>）。
/// 过期 = 放进来超过 <c>Storage:RecycleBin:RetentionDays</c> 天；那个值为 0 时这一趟什么都不做。
/// </summary>
public class RecycleBinCleanupContributor : IStorageCleanupContributor
{
    private readonly IRecycleBinService _recycleBin;

    public RecycleBinCleanupContributor(IRecycleBinService recycleBin)
    {
        _recycleBin = Check.NotNull(recycleBin);
    }

    /// <inheritdoc />
    public string Name => "ExpiredRecycleBinEntries";

    /// <inheritdoc />
    public Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default)
        => _recycleBin.PurgeExpiredAsync(maxItems, cancellationToken);
}
