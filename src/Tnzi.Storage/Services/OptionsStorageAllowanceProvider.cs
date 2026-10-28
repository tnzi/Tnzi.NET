namespace Tnzi.Storage.Services;

/// <summary>
/// 默认配额来源：每个用户都是 <c>Storage:Quota:DefaultUserAllowanceBytes</c>（每次读当前值，设置改了立即生效）。
/// </summary>
public class OptionsStorageAllowanceProvider : IStorageAllowanceProvider
{
    private readonly IOptionsMonitor<StorageOptions> _options;

    public OptionsStorageAllowanceProvider(IOptionsMonitor<StorageOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<long?> GetAllowanceAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_options.CurrentValue.Quota.DefaultUserAllowanceBytes);
}
