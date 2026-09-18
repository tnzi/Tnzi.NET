namespace Tnzi.AI.Services;

/// <summary>
/// 解析当前调用者对一组工具（工具组 + 单工具）所持有的 <c>RequiredPermissions</c> 子集。
/// 这是 Agent 工具门控的<b>唯一</b>入口：主路径（DB / ad-hoc / 工作区 agent）与多 agent 路径
/// （Handoff / Router / AgentAsTools 的子 agent）都必须经它算出 userPermissions 再交给 <see cref="IAgentFactory"/>，
/// 否则 <c>null</c> 会让注册表放弃门控、把 sandbox / task / a2a 这类受门控的工具交给任何调用者。
/// </summary>
public interface IUserToolPermissionResolver
{
    /// <summary>
    /// 汇总 <paramref name="toolGroups"/> 与 <paramref name="toolNames"/> 两路声明的权限要求，逐一检查当前调用者是否持有。
    /// </summary>
    /// <returns>
    /// <c>null</c> = 不需要门控（没有任何工具声明权限要求）；
    /// 否则返回调用者实际持有的所需权限子集 —— <b>一条都不持有时是空集，不是 null</b>，注册表据此排除全部门控工具。
    /// </returns>
    Task<IEnumerable<string>?> ResolveAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? toolNames, CancellationToken ct);
}
