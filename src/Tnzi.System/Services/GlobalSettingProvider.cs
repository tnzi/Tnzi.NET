namespace Tnzi.System.Services;

/// <summary>
/// 全局配置提供者（优先级最低）
/// </summary>
public class GlobalSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;

    public string Name => "Global";
    public int Priority => 100;

    public GlobalSettingProvider(IRepository<Setting, Guid> repository)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _repository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == SettingScope.Global)
            .FirstOrDefaultAsync(cancellationToken);

        return setting?.Value;
    }
}
