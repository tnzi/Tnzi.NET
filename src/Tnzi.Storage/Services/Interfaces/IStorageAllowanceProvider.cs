namespace Tnzi.Storage.Services;

/// <summary>
/// 一个用户能存多少字节。配额的<b>来源</b>，判定在 <see cref="IStorageQuotaService"/>。
/// </summary>
/// <remarks>
/// <para>
/// 默认实现只读 <c>Storage:Quota:DefaultUserAllowanceBytes</c>，每个用户一样。加载 <c>Tnzi.Storage.Workspace</c> 时
/// 换成它的实现：先看管理员给这个用户设的覆盖值（<c>admin/storage/quotas</c>），没有再用默认值。
/// </para>
/// <para>
/// 消费方要按自己的规则给配额（订阅套餐、角色），注册自己的实现即可，用 <c>AddScoped</c> 或
/// <c>services.Replace(...)</c>：父模块的默认实现是在 <c>PostConfigureServicesAsync</c> 里以 <c>TryAdd</c> 补上的，
/// 谁先注册谁赢；但工作区子模块在配置阶段注册了自己的那份，消费方用 <c>TryAdd</c> 会被它挡掉。
/// </para>
/// </remarks>
public interface IStorageAllowanceProvider
{
    /// <summary>
    /// 这个用户可用的字节数；<c>null</c> = 不限。
    /// </summary>
    Task<long?> GetAllowanceAsync(Guid userId, CancellationToken cancellationToken = default);
}
