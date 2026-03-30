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
    /// 获取所有配置（按分组）
    /// </summary>
    Task<Result<IEnumerable<SettingDto>>> GetSettingsAsync(string? group = null);

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
    /// 获取配置分组列表（分组名称 + 每组配置数量）
    /// </summary>
    Task<Result<List<SettingGroupDto>>> GetSettingGroupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<List<SettingGroupDto>>("Setting groups not implemented", 501));
    }
}
