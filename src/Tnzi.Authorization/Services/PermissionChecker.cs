namespace Tnzi.Authorization.Services;

/// <summary>
/// 权限检查器实现
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IFunctionAuthorizationService _functionAuthorizationService;
    private readonly ICurrentUser? _currentUser;

    public PermissionChecker(
        IFunctionAuthorizationService functionAuthorizationService,
        ICurrentUser? currentUser = null)
    {
        _functionAuthorizationService = Check.NotNull(functionAuthorizationService);
        _currentUser = currentUser;
    }

    public async Task<bool> IsGrantedAsync(string permissionName)
    {
        if (_currentUser?.Id == null)
            return false;

        return await IsGrantedAsync(_currentUser.Id.Value, permissionName);
    }

    public async Task<bool> IsGrantedAsync(Guid userId, string permissionName)
    {
        if (string.IsNullOrEmpty(permissionName))
            return false;

        return await _functionAuthorizationService.CheckPermissionAsync(userId, permissionName);
    }

    public async Task<bool> IsGrantedAnyAsync(params string[] permissionNames)
    {
        if (_currentUser?.Id == null)
            return false;

        if (permissionNames == null || permissionNames.Length == 0)
            return false;

        // 单个权限走单次检查，避免批量查询开销
        if (permissionNames.Length == 1)
            return await IsGrantedAsync(_currentUser.Id.Value, permissionNames[0]);

        // 批量查询：一次获取用户所有权限，然后 HashSet 检查
        var results = await _functionAuthorizationService.CheckPermissionsAsync(_currentUser.Id.Value, permissionNames);
        return results.Values.Any(v => v);
    }

    public async Task<bool> IsGrantedAllAsync(params string[] permissionNames)
    {
        if (_currentUser?.Id == null)
            return false;

        if (permissionNames == null || permissionNames.Length == 0)
            return true;

        // 单个权限走单次检查
        if (permissionNames.Length == 1)
            return await IsGrantedAsync(_currentUser.Id.Value, permissionNames[0]);

        // 批量查询：一次获取用户所有权限，然后 HashSet 检查
        var results = await _functionAuthorizationService.CheckPermissionsAsync(_currentUser.Id.Value, permissionNames);
        return results.Values.All(v => v);
    }

    public async Task CheckAsync(string permissionName)
    {
        var isGranted = await IsGrantedAsync(permissionName);
        if (!isGranted)
        {
            throw new ForbiddenException($"Permission '{permissionName}' is not granted.");
        }
    }
}

