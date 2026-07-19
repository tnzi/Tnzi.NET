namespace Tnzi.System.Services;

/// <summary>
/// 配置值提供者接口，支持分层配置解析
/// 多个提供者按 Priority 降序组成解析链，第一个返回非 null 的值被采用
/// </summary>
public interface ISettingProvider
{
    /// <summary>
    /// 提供者名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 优先级（越高越优先解析）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 获取配置值，未找到返回 null
    /// </summary>
    Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default);
}
