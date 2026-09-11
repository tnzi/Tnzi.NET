namespace Tnzi.System.Services;

/// <summary>
/// 配置服务接口
/// </summary>
public interface ISettingService
{
    /// <summary>
    /// 获取应用程序配置选项（从配置文件读取）
    /// </summary>
    ApplicationOptions GetApplicationOptions();

    /// <summary>
    /// 获取应用程序名称（优先从数据库，其次配置文件）
    /// </summary>
    Task<Result<string>> GetAppNameAsync();

    /// <summary>
    /// 获取站点名称（优先从数据库，其次配置文件）
    /// </summary>
    Task<Result<string>> GetSiteNameAsync();

    /// <summary>
    /// 获取配置值（从数据库读取）
    /// </summary>
    Task<Result<string?>> GetSettingAsync(string key, string? defaultValue = null);

    /// <summary>
    /// 获取配置值（从数据库读取，带类型转换）
    /// </summary>
    Task<Result<T?>> GetSettingAsync<T>(string key, T? defaultValue = default) where T : struct;

    /// <summary>
    /// 设置配置值（保存到数据库）
    /// </summary>
    Task<Result> SetSettingAsync(string key, string value, string? description = null, string? group = null);

    /// <summary>
    /// 获取配置列表（管理端）。按<b>调用者的租户</b>收口：
    /// <paramref name="scope"/> 为 null 时返回 Global 行 + 调用者本租户的 Tenant 行（宿主调用者只有 Global）；
    /// <c>Tenant</c> 时租户内调用者固定看本租户（请求别的 <paramref name="scopeId"/> 返回 403），宿主可列全部或指定一个；
    /// <c>User</c> 时租户内调用者必须给出 <paramref name="scopeId"/>（用户 id，否则 400），宿主可列全部。
    /// </summary>
    /// <remarks>
    /// <c>Setting</c> 不实现 <c>IMultiTenant</c>（租户归属在 <c>ScopeId</c>，Global 行没有租户），
    /// 全局租户过滤器管不到它；不在这里收口，租户 A 的管理员会拿到租户 B 的 Tenant 行与所有用户的 User 行。
    /// </remarks>
    Task<Result<IEnumerable<SettingDto>>> GetSettingsAsync(string? group = null, SettingScope? scope = null, string? scopeId = null);

    /// <summary>
    /// 获取配置（根据ID）
    /// </summary>
    Task<Result<SettingDto>> GetSettingByIdAsync(Guid id);

    /// <summary>
    /// 创建配置
    /// </summary>
    Task<Result<SettingDto>> CreateSettingAsync(CreateSettingDto input);

    /// <summary>
    /// 更新配置
    /// </summary>
    Task<Result<SettingDto>> UpdateSettingAsync(Guid id, UpdateSettingDto input);

    /// <summary>
    /// 删除配置
    /// </summary>
    Task<Result> DeleteSettingAsync(Guid id);

    /// <summary>
    /// 批量删除配置
    /// </summary>
    Task<Result> DeleteSettingsAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 获取配置值（分层解析：User → Tenant → Global）
    /// 使用注册的 ISettingProvider 链按优先级解析
    /// </summary>
    Task<Result<string?>> GetSettingValueAsync(string key, string? defaultValue = null);

    /// <summary>
    /// 获取指定作用域的配置值
    /// </summary>
    Task<Result<string?>> GetSettingAsync(string key, SettingScope scope, string? scopeId = null);

    /// <summary>
    /// 设置指定作用域的配置值
    /// </summary>
    Task<Result> SetSettingAsync(string key, string value, SettingScope scope, string? scopeId = null);

    /// <summary>
    /// Set an encrypted setting value (encrypts before saving, sets IsEncrypted=true)
    /// </summary>
    Task<Result> SetEncryptedAsync(string group, string key, string value, string? description = null);

    /// <summary>
    /// Get decrypted setting value (decrypts if IsEncrypted=true)
    /// </summary>
    Task<Result<string?>> GetDecryptedAsync(string group, string key);

    /// <summary>
    /// Get system information (version, loaded modules, uptime, environment)
    /// </summary>
    Task<Result<SystemInfoDto>> GetSystemInfoAsync()
    {
        return Task.FromResult(Result.Failure<SystemInfoDto>("Not implemented", 501));
    }

    /// <summary>
    /// 获取配置分组列表（分组名称 + 每组配置数量）。作用域口径与 <see cref="GetSettingsAsync"/> 完全一致，
    /// 计数必须与列表对得上。
    /// </summary>
    Task<Result<List<SettingGroupDto>>> GetSettingGroupsAsync(SettingScope? scope = null, string? scopeId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<List<SettingGroupDto>>("Setting groups not implemented", 501));
    }

    /// <summary>
    /// 重排同一分组内的配置项（拖拽排序）
    /// </summary>
    /// <param name="ids">按新顺序排列的配置 Id，可以只是当前可见的一段</param>
    /// <param name="group">分组范围；null = 未分组的配置项</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, string? group = null, CancellationToken cancellationToken = default);
}
