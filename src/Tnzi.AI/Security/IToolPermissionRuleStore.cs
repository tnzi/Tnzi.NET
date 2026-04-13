namespace Tnzi.AI.Security;

/// <summary>
/// 工具权限规则持久化存储接口
/// </summary>
public interface IToolPermissionRuleStore
{
    /// <summary>
    /// 获取所有已启用的持久化权限规则
    /// </summary>
    Task<IReadOnlyList<ToolPermissionRule>> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 Scope 获取已启用的持久化权限规则
    /// </summary>
    Task<IReadOnlyList<ToolPermissionRule>> GetRulesByScopeAsync(ToolPermissionScope scope, CancellationToken cancellationToken = default);
}
