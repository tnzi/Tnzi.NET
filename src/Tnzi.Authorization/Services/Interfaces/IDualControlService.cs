namespace Tnzi.Authorization.Services;

/// <summary>
/// 双人授权（四眼原则）：某些动作必须由第二个人点头才能执行。
/// </summary>
/// <remarks>
/// <para>
/// <b>三步，不是两步</b>：发起 → 批准 → 执行。批准只产生一张许可，动作仍由发起方回来执行。
/// 这样框架不必知道每种业务动作怎么做，审批人也不必承担执行本身的后果。
/// </para>
/// <para>
/// 典型用法：
/// <code>
/// // 发起
/// var request = await dualControl.RequestAsync(new("finance.payrun.void", payRunId, payload));
///
/// // …另一个人批准…
///
/// // 执行前取用许可，参数必须与当初批的那份逐字相同
/// var permit = await dualControl.ConsumeAsync(requestId, "finance.payrun.void", payload);
/// if (!permit.Succeeded) return permit.ToApiResult();
/// await DoTheThing();
/// </code>
/// </para>
/// <para>
/// <b>两条守卫，一条在框架、一条要你自己声明</b>：框架保证<b>批准人不是发起人</b>（四眼原则本身），
/// 并按 <c>{Operation}.approve</c> 查一次权限（后缀可配，空串关闭）。
/// 那些权限码<b>要由应用自己声明</b> —— 框架不知道你有哪些需要双人授权的动作。
/// 没声明的码没有人持有，因此除超管外一律拒绝。
/// </para>
/// <para>
/// ★ <b>本服务也不负责通知审批人。</b>发起时会发一个事件，接不接、怎么送达（站内信、邮件、
/// 值班电话）由应用决定 —— 送达策略与业务紧要程度有关，框架猜不出来。
/// </para>
/// </remarks>
public interface IDualControlService
{
    /// <summary>
    /// 发起一个待批准的动作。
    /// </summary>
    /// <remarks>
    /// <b>同一发起人</b>对同一动作 + 同一目标、带<b>同一份参数</b>已有未决请求时直接返回那一条，不重复发起 ——
    /// 否则连点两下按钮会造出两张许可，而「两张许可」等于这个动作被批准了两次。
    /// 去重键刻意含发起人与参数：别人的待批请求（连同参数原文）不该递给你，
    /// 而改了参数再发起是一次新的请求，不能静默换成旧参数那张。
    /// </remarks>
    Task<Result<DualControlRequestDto>> RequestAsync(DualControlRequestInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批准一个请求。
    /// </summary>
    /// <remarks>
    /// ★ <b>批准人是发起人时一律拒绝</b>，这是四眼原则的全部内容。
    /// 随后按 <c>{Operation}.approve</c> 查一次权限（见 <c>DualControlOptions.ApprovalPermissionSuffix</c>）。
    /// </remarks>
    Task<Result<DualControlRequestDto>> ApproveAsync(Guid requestId, string? comment = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拒绝一个请求。
    /// </summary>
    Task<Result<DualControlRequestDto>> RejectAsync(Guid requestId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 由发起人撤回一个未决请求。
    /// </summary>
    Task<Result> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取用一张许可：校验它已批准、未过期、未用过、<b>取用者就是发起人</b>，且参数与当初批的那份一致。
    /// </summary>
    /// <param name="requestId">请求标识。</param>
    /// <param name="operation">动作标识，必须与发起时一致。</param>
    /// <param name="payloadJson">
    /// 执行时的参数。<b>必须与发起时逐字相同</b>，否则拒绝 ——
    /// 少了这一比，批下来的「转 100 元」可以改成「转 100 万」再执行。
    /// 传 <c>null</c> 表示这个动作没有参数，此时要求发起时也没有。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// <b>成功即消费，一张许可换一次执行。</b>调用方拿到成功之后应当立刻执行，
    /// 且执行失败不会退回许可 —— 退回意味着「这次批准还能再用一次」，
    /// 而框架无从判断上一次到底做到了哪一步。
    /// </remarks>
    Task<Result<DualControlRequestDto>> ConsumeAsync(
        Guid requestId,
        string operation,
        string? payloadJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询一个请求。
    /// </summary>
    Task<Result<DualControlRequestDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询请求。
    /// </summary>
    Task<Result<IPagedList<DualControlRequestDto>>> QueryAsync(DualControlQueryDto query, CancellationToken cancellationToken = default);
}
