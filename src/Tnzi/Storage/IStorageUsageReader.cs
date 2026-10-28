namespace Tnzi.Storage;

/// <summary>
/// 一个范围（整个安装、一个租户、一个用户）此刻在存储里占了多少字节（<b>可选契约</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 按存储量计费的一方（订阅的超额存储费）不该引用 <c>Tnzi.Storage</c>，所以这个问题在核心问；
/// 实现随 <c>Tnzi.Storage</c> 一起注册，未加载存储模块时解析不到它，调用方按「量不出来」处理（不计费、记日志），
/// 绝不当作 0 字节。
/// </para>
/// <para>
/// 计入的与存储配额同一个口径：每一条文件记录（含临时上传、回收站里尚未清除的、系统渲染的产物），
/// 加上各 <c>IStorageUsageContributor</c> 报的字节（工作区的历史版本），再加上缩略图。
/// </para>
/// <para>
/// ★ 默认（<see cref="StorageUsageReadMode.Measure"/>）实现会顺带<b>量一次</b>尚未记下大小的对象（缩略图的大小在生成时没有记录，
/// 第一次被问到时向存储提供者查一次并记下），每次调用量的个数有上限、每个一次提供者往返：那是给后台任务（每天一次的用量采样）用的。
/// 请求路径上用 <see cref="StorageUsageReadMode.KnownSizesOnly"/>：只对已知大小求和，一条聚合查询。
/// </para>
/// </remarks>
public interface IStorageUsageReader
{
    /// <summary>
    /// <paramref name="scope"/> 此刻占用的字节数。
    /// </summary>
    /// <param name="scope">范围。</param>
    /// <param name="mode">要不要顺带量尚未记下大小的对象（见 <see cref="StorageUsageReadMode"/>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<long> GetStoredBytesAsync(StorageUsageScope scope, StorageUsageReadMode mode = StorageUsageReadMode.Measure, CancellationToken cancellationToken = default);
}

/// <summary>读存储用量时怎么对待尚未记下大小的对象（缩略图）。</summary>
public enum StorageUsageReadMode
{
    /// <summary>先向存储提供者量一批尚未记下大小的对象并记下，再求和。给后台采样用：可能要几百次提供者往返。</summary>
    Measure = 0,

    /// <summary>只对已知大小求和（还没量过的缩略图按 0 计），一条聚合查询。给请求路径上的显示用。</summary>
    KnownSizesOnly = 1
}

/// <summary>按什么范围算存储用量。</summary>
public enum StorageUsageScopeKind
{
    /// <summary>整个安装（不分租户）。</summary>
    Installation = 0,

    /// <summary>一个租户的全部文件。</summary>
    Tenant = 1,

    /// <summary>一个用户名下的文件（与存储配额同一判据：文件记录的创建人）。</summary>
    User = 2
}

/// <summary>
/// 存储用量的范围。
/// </summary>
/// <param name="Kind">范围种类。</param>
/// <param name="Id">租户或用户的 Id；<see cref="StorageUsageScopeKind.Installation"/> 时为空。</param>
public sealed record StorageUsageScope(StorageUsageScopeKind Kind, Guid? Id = null)
{
    /// <summary>整个安装。</summary>
    public static StorageUsageScope Installation { get; } = new(StorageUsageScopeKind.Installation);

    /// <summary>一个租户。</summary>
    public static StorageUsageScope ForTenant(Guid tenantId) => new(StorageUsageScopeKind.Tenant, tenantId);

    /// <summary>一个用户。</summary>
    public static StorageUsageScope ForUser(Guid userId) => new(StorageUsageScopeKind.User, userId);
}
