namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 加载本模块时的配额来源：管理员给这个用户设过覆盖值就用它（覆盖值为空 = 这个用户不限），
/// 否则用 <c>Storage:Quota:DefaultUserAllowanceBytes</c>。
/// </summary>
public class StorageAllowanceProvider : IStorageAllowanceProvider
{
    private readonly IRepository<StorageAllowance, Guid> _allowanceRepository;
    private readonly IOptionsMonitor<StorageOptions> _options;

    public StorageAllowanceProvider(IRepository<StorageAllowance, Guid> allowanceRepository, IOptionsMonitor<StorageOptions> options)
    {
        _allowanceRepository = Check.NotNull(allowanceRepository);
        _options = Check.NotNull(options);
    }

    public async Task<long?> GetAllowanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var overrideRow = await _allowanceRepository.AsQueryable()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.AllowanceBytes })
            .FirstOrDefaultAsync(cancellationToken);

        return overrideRow != null
            ? overrideRow.AllowanceBytes
            : _options.CurrentValue.Quota.DefaultUserAllowanceBytes;
    }
}
