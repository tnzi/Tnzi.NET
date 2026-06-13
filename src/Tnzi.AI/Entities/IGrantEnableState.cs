namespace Tnzi.AI.Entities;

/// <summary>
/// 标记接口 — 暴露 grant junction 实体的 <see cref="IsEnabled"/> 启用状态，
/// 让 <c>AgentGrantService</c> 的 reconcile 可以泛型地"重新启用被禁用但仍被请求"的条目。
/// Marker interface exposing the <see cref="IsEnabled"/> flag of a grant junction entity so that
/// <c>AgentGrantService</c> reconcile can generically re-enable disabled-but-still-desired grants.
/// </summary>
public interface IGrantEnableState
{
    /// <summary>是否启用本条授权。Whether this grant is enabled.</summary>
    bool IsEnabled { get; set; }
}
