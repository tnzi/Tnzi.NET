namespace Tnzi.Authorization.Options;

/// <summary>
/// 双人授权（四眼原则）配置。配置路径 <c>Authorization:DualControl</c>。
/// </summary>
/// <remarks>
/// 这里<b>没有「哪些动作需要双人授权」的清单</b>：那是业务决定，写在代码里
/// （调用 <see cref="Services.IDualControlService"/> 的那一处），不该能被改配置绕开。
/// </remarks>
[ConfigSection("Authorization:DualControl")]
public class DualControlOptions
{
    /// <summary>
    /// 许可的默认有效期（分钟），默认 <c>1440</c>（一天）。
    /// </summary>
    /// <remarks>
    /// 发起时可以逐个覆盖。默认给一天是因为审批人未必在线，
    /// 而一张过期太快的许可会逼着发起方反复重发，最后变成走过场。
    /// </remarks>
    public int DefaultLifetimeMinutes { get; set; } = 1440;

    /// <summary>
    /// 批准权限码的后缀，默认 <c>".approve"</c>。空串表示不由服务层检查。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 服务层按 <c>{Operation}{此后缀}</c> 检查当前用户 —— 于是「谁能批准作废工资单」
    /// （<c>finance.payrun.void.approve</c>）与「谁能批准删账户」是两个可以分别授予的码，
    /// 而不是一个「能批准任何东西」的通行证。
    /// </para>
    /// <para>
    /// ★ <b>这些码要由应用自己声明</b>（<c>IPermissionDefinitionProvider</c>）——
    /// 框架不知道你有哪些需要双人授权的动作。没声明的码没有人持有，因此除超管外一律拒绝：
    /// 这与本模块 deny-by-default 的立场一致，但上线前要记得把码加进目录并授权，
    /// 否则审批人会看到一个永远点不动的批准按钮。
    /// </para>
    /// <para>
    /// 配成空串是显式选择「我在自己的批准端点上把关」，此时服务层只保证「批准人不是发起人」。
    /// </para>
    /// </remarks>
    public string ApprovalPermissionSuffix { get; set; } = ".approve";
}
