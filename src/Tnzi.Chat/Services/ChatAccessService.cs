namespace Tnzi.Chat.Services;

/// <summary>
/// <see cref="IChatAccessService"/> 默认实现。判定完全委托权限系统的
/// <see cref="IPermissionChecker"/>（超管 bypass 自然使超管可用）；
/// 通过可选注入的 <see cref="IFunctionAuthorizationService"/> 是否在场判断
/// Authorization 模块是否加载——不在场则 fail-open（无 gate），避免独立运行的
/// Chat 因「无人持 chat.use」而整体失效。
/// </summary>
public class ChatAccessService : ApplicationService, IChatAccessService
{
    /// <summary>使用聊天所需的权限码（白名单，deny-by-default）。</summary>
    public const string UsePermission = "chat.use";

    private readonly IPermissionChecker? _permissionChecker;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public ChatAccessService(
        IServiceProvider serviceProvider,
        IPermissionChecker? permissionChecker = null,
        IFunctionAuthorizationService? functionAuthorization = null) : base(serviceProvider)
    {
        _permissionChecker = permissionChecker;
        _functionAuthorization = functionAuthorization;
    }

    // Authorization present → a real PermissionChecker gates chat. Absent → nothing
    // to gate against (a NullPermissionChecker would deny everyone), so fail-open.
    private bool GateActive => _functionAuthorization is not null && _permissionChecker is not null;

    public async Task<bool> CanUseAsync(Guid userId)
    {
        if (!GateActive) return true;
        if (userId == Guid.Empty) return false;
        return await _permissionChecker!.IsGrantedAsync(userId, UsePermission);
    }

    public Task<bool> CanCurrentUserUseAsync()
    {
        var id = CurrentUser?.Id;
        return id is null ? Task.FromResult(false) : CanUseAsync(id.Value);
    }

    public async Task<IReadOnlySet<Guid>> FilterDisabledAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        var disabled = new HashSet<Guid>();
        if (!GateActive || ids.Count == 0) return disabled;

        foreach (var id in ids)
        {
            if (!await _permissionChecker!.IsGrantedAsync(id, UsePermission))
                disabled.Add(id);
        }
        return disabled;
    }
}
