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

    /// <summary>
    /// 名单里**没有** <c>chat.use</c> 的子集。
    ///
    /// ★ 走 <see cref="IFunctionAuthorizationService.FilterGrantedAsync"/> 一次批量判定，
    /// 不是逐个 <c>IsGrantedAsync</c> 循环：每次单查至少一次无缓存的角色查询，而通讯录搜索、
    /// 群发、会话列表都经这里 —— 逐个循环会把一个请求放大成 O(N) 次 DB 往返。
    /// 与 <see cref="CanUseAsync"/> 同一判定源（<c>PermissionChecker</c> 本就委托给它）。
    /// </summary>
    public async Task<IReadOnlySet<Guid>> FilterDisabledAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        var disabled = new HashSet<Guid>();
        if (!GateActive || ids.Count == 0) return disabled;

        var granted = await _functionAuthorization!.FilterGrantedAsync(ids, UsePermission)
            ?? new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (!granted.Contains(id)) disabled.Add(id);
        }
        return disabled;
    }
}
