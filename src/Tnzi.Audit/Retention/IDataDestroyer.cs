namespace Tnzi.Audit.Retention;

/// <summary>
/// 到期数据的销毁动作。
/// </summary>
/// <remarks>
/// <para>
/// 框架自带 <see cref="HardDeleteDataDestroyer"/>（硬删除）。要改成匿名化、
/// 转存冷归档或别的处置方式，注册自己的实现覆盖它：
/// <code>
/// context.Services.AddScoped&lt;IDataDestroyer, AnonymizingDataDestroyer&gt;();
/// </code>
/// </para>
/// <para>
/// <strong>销毁必须是彻底的。</strong>实现方要留意软删除：把 <c>IsDeleted</c> 置真
/// 只是让数据在应用里看不见，行还在库里、也还在备份里，这不叫销毁。
/// 框架自带实现因此显式走硬删除路径。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public interface IDataDestroyer
{
    /// <summary>
    /// 销毁方式标识，会原样写进销毁证明。
    /// </summary>
    /// <remarks>
    /// 一份说不清「销毁」到底做了什么的证明没有价值。换了销毁器就要换这个标识，
    /// 否则历史证明会声称新旧记录是用同一种方式处置的。
    /// </remarks>
    string Mode { get; }

    /// <summary>
    /// 销毁给定的一批记录。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <param name="entities">本批要销毁的记录（已排除诉讼保全的部分）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际销毁的条数。</returns>
    /// <remarks>
    /// 抛异常会中止该策略本轮的销毁，且<strong>不会</strong>写出销毁证明——
    /// 一份声称销毁成功而实际失败的证明比没有证明更糟。
    /// </remarks>
    Task<int> DestroyAsync<TEntity>(
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity;
}
