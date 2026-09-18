namespace Tnzi.Audit.Services;

/// <summary>
/// 审计读取面的租户收口：调用者有租户则钉在本租户，宿主看全部。
/// </summary>
/// <remarks>
/// <para>
/// 三张审计表（<see cref="AuditOperation"/> / <see cref="AuditRecordAccess"/> / <see cref="AuditDataDestruction"/>）
/// 刻意不实现 <c>IMultiTenant</c>（审计写入不该被租户过滤器挡住），租户归属是手工写入的 <c>TenantId</c> 列，
/// 所以全局租户过滤器管不到它们，读取侧要自己加谓词。租户由<b>身份</b>决定
/// （<see cref="ICurrentTenant"/> → <c>ICurrentUser.TenantId</c>，与 <c>SettingService</c> 同源），不由客户端参数决定。
/// </para>
/// <para>
/// ★ 以多租户开关为闸门：<c>TenantId</c> 在多租户关闭时被 <c>builder.Ignore</c>（三个实体配置），
/// 对被 Ignore 的属性写 <c>Where</c> 会在查询翻译时抛异常 —— 关闭时必须一行谓词都不加，
/// 否则单租户部署里每次审计查询都 500，比缺陷本身更糟。<see cref="IsPinned"/> 在关闭时恒为 false。
/// </para>
/// </remarks>
internal readonly struct CallerTenantScope
{
    private CallerTenantScope(Guid? tenantId)
    {
        TenantId = tenantId;
    }

    /// <summary>调用者所属租户；宿主 / 多租户关闭 / 无租户 claim 时为 null。</summary>
    public Guid? TenantId { get; }

    /// <summary>调用者是否被钉在某个租户上（只有多租户开启且调用者带租户时为真）。</summary>
    public bool IsPinned => TenantId.HasValue;

    /// <summary>
    /// 解析调用者的租户作用域。
    /// </summary>
    /// <param name="multiTenancyOptions">多租户开关；缺席按关闭。</param>
    /// <param name="currentTenant">当前租户访问器；缺席退回当前用户的租户 claim。</param>
    /// <param name="currentUser">当前用户；缺席（后台作用域）视作宿主。</param>
    public static CallerTenantScope Resolve(IOptions<MultiTenancyOptions>? multiTenancyOptions, ICurrentTenant? currentTenant, ICurrentUser? currentUser)
    {
        var enabled = multiTenancyOptions?.Value.Enabled ?? false;
        return enabled
            ? new CallerTenantScope(currentTenant?.Id ?? currentUser?.TenantId)
            : default;
    }

    /// <summary>某一行（按其 <c>TenantId</c> 列）对调用者是否可见：别的租户的行等同于不存在。</summary>
    public bool CanAccess(Guid? rowTenantId) => !IsPinned || rowTenantId == TenantId;
}
