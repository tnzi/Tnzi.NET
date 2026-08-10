namespace Tnzi.Audit.Retention;

/// <summary>
/// 诉讼保全：即使保留期已届满，也必须暂缓销毁的记录。
/// </summary>
/// <remarks>
/// <para>
/// 保留期到了并不总是意味着可以销毁。案子在打官司、监管在调查、
/// 或者当事人提出了查阅请求时，销毁到期数据反而会构成销毁证据。
/// 这类豁免是<strong>动态</strong>的（今天保全、下个月解除），
/// 因此不能写进 <c>RetentionPolicy.Scope</c>——那是要翻译成 SQL 的静态条件。
/// </para>
/// <para>
/// <strong>不注册任何实现时视为没有保全</strong>，销毁照常进行。
/// 这是刻意的默认：一个没有诉讼保全需求的应用不该被迫实现一个恒返回空集的类。
/// 但对确实会遇到诉讼的业务，<strong>漏接这个契约的后果是销毁掉本该保留的证据</strong>。
/// </para>
/// <para>
/// 可以注册多个实现，任一实现认为该保全的记录都会被跳过（并集）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public interface ILitigationHoldProvider
{
    /// <summary>
    /// 从候选集合中挑出必须暂缓销毁的记录。
    /// </summary>
    /// <param name="policyName">触发本次销毁的策略标识。</param>
    /// <param name="entityType">候选记录的实体类型。</param>
    /// <param name="candidateIdentifiers">本批到期记录的标识（主键的字符串形式）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// <paramref name="candidateIdentifiers"/> 的子集：其中需要保全的那些。
    /// 返回空集表示这一批都可以销毁。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 传入的是<strong>整批候选</strong>而不是逐条询问，实现方因此可以用一条
    /// <c>WHERE Id IN (...)</c> 解决，不必为一批 500 条发 500 次查询。
    /// </para>
    /// <para>
    /// <strong>这个方法抛异常会中止该策略本轮的销毁</strong>，而不是当作「无保全」继续。
    /// 保全系统查不通时宁可不销毁：晚一天销毁只是延迟，销毁了不该销毁的无法撤销。
    /// </para>
    /// </remarks>
    Task<IReadOnlyCollection<string>> GetHeldIdentifiersAsync(
        string policyName,
        Type entityType,
        IReadOnlyCollection<string> candidateIdentifiers,
        CancellationToken cancellationToken = default);
}
