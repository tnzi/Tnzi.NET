namespace Tnzi.Audit.Retention;

/// <summary>
/// 声明本应用的数据保留策略。
/// </summary>
/// <remarks>
/// <para>
/// 在模块的 <c>ConfigureServicesAsync</c> 里注册实现：
/// <code>
/// context.Services.AddTransient&lt;IRetentionPolicyProvider, TipRetentionPolicies&gt;();
/// </code>
/// 可以注册多个，框架会合并所有提供者返回的策略。
/// </para>
/// <para>
/// <strong>没有任何提供者时销毁服务什么也不做</strong>（并在启动时记一条 Warning：
/// 开了开关却没有策略，多半是接线漏了，而这类漏接的表现是「安静地什么都不销毁」）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public interface IRetentionPolicyProvider
{
    /// <summary>
    /// 返回本提供者声明的全部策略。
    /// </summary>
    /// <remarks>
    /// 每次扫描都会调用，因此可以在这里从配置读取期限值。
    /// 但<strong>策略集合本身应当是稳定的</strong>：策略时有时无会让证明链出现无法解释的空档。
    /// </remarks>
    IEnumerable<RetentionPolicy> GetPolicies();
}
