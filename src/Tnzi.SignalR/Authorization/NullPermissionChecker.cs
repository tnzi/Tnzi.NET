namespace Tnzi.SignalR.Authorization;

/// <summary>
/// 无操作权限检查器。当未加载 Authorization 模块时，由 SignalR 模块 TryAdd 注册，确保 HubAuthorizationFilter 可解析。
/// 所有检查均拒绝（返回 false）。
/// </summary>
internal sealed class NullPermissionChecker : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(false);
    public Task<bool> IsGrantedAsync(Guid userId, string permissionName) => Task.FromResult(false);
    public Task<bool> IsGrantedAnyAsync(params string[] permissionNames) => Task.FromResult(false);
    public Task<bool> IsGrantedAllAsync(params string[] permissionNames) => Task.FromResult(false);
    public Task CheckAsync(string permissionName) => throw new ForbiddenException("Authorization module is not loaded.");
}
