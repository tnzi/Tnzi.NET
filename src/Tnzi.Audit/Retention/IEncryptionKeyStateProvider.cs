namespace Tnzi.Audit.Retention;

/// <summary>
/// 加密密钥存活状态提供者：回答「这个密钥标识对应的密钥是否已经不存在了」。
/// </summary>
/// <remarks>
/// <para>
/// 销毁证明里有一栏记录「这批数据所用的加密密钥是否已销毁」（<c>AuditDataDestruction.IsKeyDestroyed</c>）。
/// 框架<strong>不销毁密钥</strong>——密钥在配置、KMS 或硬件模块里，那是部署方的领地——
/// 只在每次销毁时回查一次并如实记录。本契约就是那次回查。
/// </para>
/// <para>
/// <strong>为什么要可替换。</strong>框架自带的实现只认识自己的配置密钥环。
/// 密钥放在云端密钥管理服务或自有硬件模块里的部署，以及<em>根本不启用字段级加密</em>的部署
/// （例如加密发生在客户端、服务端只存密文），在自带实现下会永远得到「密钥还在」。
/// 那个恒假的值会被算进销毁证明的哈希链，等于把一个错误结论固化进证据链——
/// 它让「加密删除到底完成没有」在证明上永远无法回答，而那正是这份证明存在的理由。
/// </para>
/// <para>
/// <strong>替换方式：</strong>注册自己的实现即可覆盖默认实现。
/// <code>
/// context.Services.AddScoped&lt;IEncryptionKeyStateProvider, KmsKeyStateProvider&gt;();
/// </code>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "销毁证明的密钥字段仍在演进，可能补充销毁时间与销毁执行者")]
public interface IEncryptionKeyStateProvider
{
    /// <summary>
    /// 判断给定密钥是否已确认销毁。
    /// </summary>
    /// <param name="keyId">密钥标识，来自保留策略声明。</param>
    /// <returns>
    /// 已确认销毁返回 <c>true</c>；<strong>无法确认时一律返回 <c>false</c></strong>。
    /// </returns>
    /// <remarks>
    /// <strong>不确定必须返回 <c>false</c>。</strong>这一栏进入的是证据链，
    /// 而「查不到所以大概销毁了」和「确认已销毁」在法庭上是两回事。
    /// 盖一个没资格盖的章，比留一个如实的「否」危险得多。
    /// 实现方在远端不可达、凭据失效或密钥标识无法解析时，都应当返回 <c>false</c> 而不是抛异常——
    /// 一次回查失败不该让整轮销毁中止。
    /// </remarks>
    bool IsDestroyed(string keyId);
}
