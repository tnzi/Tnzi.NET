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
    /// 获取配置值，这一级没有配置时返回 null（解析链随之询问下一级）。
    /// </summary>
    /// <remarks>
    /// 这一级<b>有</b>一条加密值却解不开（加密器缺席或密钥对不上）时不要返回 null：那会让解析链悄悄落到低一级作用域的另一个值上。
    /// 内置的三个 provider 此时抛异常，<c>ISettingService.GetSettingValueAsync</c> 据此答失败（500 + 配置错误码）而不是默认值。
    /// </remarks>
    Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default);
}
