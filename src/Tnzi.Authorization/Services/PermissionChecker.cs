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

        foreach (var permissionName in permissionNames)
        {
            if (await IsGrantedAsync(_currentUser.Id.Value, permissionName))
                return true;
        }

        return false;
    }

    public async Task<bool> IsGrantedAllAsync(params string[] permissionNames)
    {
        if (_currentUser?.Id == null)
            return false;

        if (permissionNames == null || permissionNames.Length == 0)
            return true;

        foreach (var permissionName in permissionNames)
        {
            if (!await IsGrantedAsync(_currentUser.Id.Value, permissionName))
                return false;
        }

        return true;
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

