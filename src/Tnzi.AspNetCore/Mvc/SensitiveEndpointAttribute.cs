namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 标记一个默认激活的端点为<strong>敏感端点</strong>：它开着的后果，比"多了一个可调用的方法"更重。
/// </summary>
/// <remarks>
/// <para>
/// <strong>为什么需要这个特性。</strong>框架的 <c>[DefaultController]</c> 是自动激活的 ——
/// 应用什么都不做，模块自带的端点就挂上了路由。这在多数项目里是优点，在高安全项目里是危险默认值：
/// 部署方<strong>没有办法知道自己开了什么</strong>，只能去读每个模块的源码列清单。
/// </para>
/// <para>
/// ★★ <strong>问题不是"审查麻烦"，是"审查必然会漏"。</strong>逐模块人工过一遍是一次性的劳动，
/// 而框架会继续演进：下一个版本给某个模块加了一个新的敏感端点，
/// <strong>没有任何机制会提醒已经审过的应用重审一遍</strong>。
/// 标了这个特性，它就会自动出现在 <c>GET admin/diagnostics/sensitive-endpoints</c> 的清单里 ——
/// 一次性的人工劳动因此变成一次查询。
/// </para>
/// <para>
/// <strong>标什么。</strong>判据是"知道它开着，会改变一个安全评审的结论吗"。典型的几类：
/// 签发能离开受控环境的 URL 或令牌、批量导出、不可撤销的破坏性操作。
/// <strong>不要给普通的读写端点乱标</strong> —— 清单一旦变长就没人读了，那等于回到没有清单。
/// </para>
/// <para>
/// <strong>怎么关。</strong>把 <see cref="Name"/> 配进 <c>AspNetCore:ControllerFilter:DisabledEndpoints</c>
/// 即可单独摘掉这一个端点，不必替换整个控制器（那会连同它的正常端点一起关掉）。
/// </para>
/// <para>
/// ★ <strong>抑制路由不等于关上了这条能力。</strong>控制器是可被消费方整体替换的，
/// 挂在它上面的任何特性都会随之失效；真正的判定必须落在服务层
/// （存储模块的例子是 <c>IFileAccessAuthorizer</c>）。本特性解决的是<strong>可见性</strong>，
/// 不是授权。把"已从清单里摘除"当成"已经安全"，是这套机制唯一容易被误用的地方。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpGet("{id:guid}/presigned-url")]
/// [SensitiveEndpoint(
///     "storage.presigned-url",
///     "Issues a URL served directly by the object store; it bypasses application-layer authorization and its lifetime is governed by the storage provider, not by the framework.")]
/// public virtual Task&lt;ApiResult&lt;string&gt;&gt; GetPresignedUrl(Guid id) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
[ExperimentalApi(Reason = "敏感端点的分类维度仍在演进，可能补充风险等级或按类别筛选")]
public sealed class SensitiveEndpointAttribute : Attribute
{
    /// <summary>
    /// 初始化一个 <see cref="SensitiveEndpointAttribute"/> 类型的新实例。
    /// </summary>
    /// <param name="name">
    /// 能力名，约定 <c>{模块}.{能力}</c>（例如 <c>storage.presigned-url</c>）。
    /// 它同时是 <c>DisabledEndpoints</c> 配置里用来摘除这个端点的键，
    /// <strong>因此一旦发布就不应再改</strong>：改了名字，既有部署的抑制配置会静默失效。
    /// </param>
    /// <param name="reason">
    /// 为什么它敏感，写给读清单的人看。要说清<strong>后果</strong>而不是复述端点做什么，
    /// 一句"签发预签名 URL"对做安全评审的人没有价值。
    /// </param>
    public SensitiveEndpointAttribute(string name, string reason)
    {
        Name = Check.NotNullOrWhiteSpace(name);
        Reason = Check.NotNullOrWhiteSpace(reason);
    }

    /// <summary>
    /// 能力名，也是 <c>DisabledEndpoints</c> 的抑制键。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 敏感的理由（后果导向）。
    /// </summary>
    public string Reason { get; }
}
