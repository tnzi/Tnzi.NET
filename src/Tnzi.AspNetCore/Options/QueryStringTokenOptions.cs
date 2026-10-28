namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 哪些路径接受经查询参数 <c>access_token</c> 携带的 Bearer 令牌。
/// </summary>
/// <remarks>
/// 浏览器的 WebSocket 与 SSE 握手无法自定义请求头，JS 客户端只能把 JWT 放进查询串。只有登记在这里的路径会读它，
/// 其余路径一律只认 Authorization 头：令牌出现在 URL 里会进访问日志、浏览器历史与 Referer，对所有路径放开等于把这个风险
/// 扩到整个 API（请求日志与审计按参数名把 <c>access_token</c> 脱敏，见 <c>QueryStringRedactor</c>）。
/// <para>
/// 默认只有 SignalR Hub 的 <c>/hubs</c>。挂了自己的浏览器实时端点的模块在注册服务时把路径加进来
///（<c>services.Configure&lt;QueryStringTokenOptions&gt;(o =&gt; o.PathPrefixes.Add("/ws/gateway"))</c>），
/// 否则浏览器客户端在这个端点上永远是匿名的。读取由 Identity 模块的 JWT 处理器完成；没装 Identity 时这份配置不起作用。
/// </para>
/// </remarks>
public class QueryStringTokenOptions
{
    /// <summary>携带令牌的查询参数名。</summary>
    public const string ParameterName = "access_token";

    /// <summary>默认登记的路径前缀：SignalR Hub。</summary>
    public const string HubsPathPrefix = "/hubs";

    /// <summary>
    /// 接受查询串令牌的路径前缀，按路径段匹配（<c>/hubs</c> 匹配 <c>/hubs/chat</c>，不匹配 <c>/hubsx</c>）。
    /// 不以 <c>/</c> 开头的项不匹配任何路径。
    /// </summary>
    public List<string> PathPrefixes { get; } = [HubsPathPrefix];

    /// <summary>该请求路径是否接受查询串里的令牌。</summary>
    public bool AcceptsQueryToken(PathString path)
        => PathPrefixes.Any(prefix => prefix is ['/', ..]
                                      && path.StartsWithSegments(new PathString(prefix), StringComparison.OrdinalIgnoreCase));
}
