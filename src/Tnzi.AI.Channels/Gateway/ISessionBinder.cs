namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 会话绑定解析器 - 根据规则将入站请求映射到 Agent + Scope + SessionKey
/// </summary>
[ExperimentalApi(Reason = "Gateway API under active development")]
public interface ISessionBinder
{
    /// <summary>
    /// 解析绑定：按优先级匹配规则，生成 SessionBinding
    /// </summary>
    SessionBinding Resolve(SessionBindingContext context);
}
