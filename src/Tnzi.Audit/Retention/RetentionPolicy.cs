using System.Linq.Expressions;

namespace Tnzi.Audit.Retention;

/// <summary>
/// 一条数据保留策略：某类记录保留多久，到期后销毁。
/// </summary>
/// <remarks>
/// <para>
/// <strong>策略在代码里声明，不在配置里。</strong>保留期通常来自法规或与监管方签订的协议，
/// 把它做成运行时可改的配置项，等于给「悄悄延长保留期」留了一扇不留痕迹的门。
/// 期限值本身要从配置读是消费方的自由，但<strong>哪些数据受策略约束</strong>必须写在代码里。
/// </para>
/// <para>
/// 用泛型子类 <see cref="RetentionPolicy{TEntity}"/> 构造，本类只承载与实体类型无关的元数据。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public abstract class RetentionPolicy
{
    /// <summary>
    /// 策略标识，会写进销毁证明。
    /// </summary>
    /// <remarks>
    /// <strong>一经上线不要改名</strong>：改了之后，同一批数据在证明链上会以两个名字出现，
    /// 事后没人能确定它们是不是同一条策略。
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>
    /// 保留期。记录的时间戳早于 <c>now - RetentionPeriod</c> 时视为到期。
    /// </summary>
    public required TimeSpan RetentionPeriod { get; init; }

    /// <summary>
    /// 这批数据所用的字段加密密钥标识（可选）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 填了它，销毁证明里会记下这个标识，并在每次销毁时回查该密钥是否仍在密钥环里，
    /// 把结果记进证明的 <c>IsKeyDestroyed</c>。
    /// </para>
    /// <para>
    /// <strong>框架不会替你销毁密钥。</strong>密钥在配置提供程序或 KMS 里，那是部署方的领地；
    /// 框架能做的是告诉你该销毁哪一把、并在事后核实它确实不见了。
    /// 只有密钥也销毁了，「已销毁」才对<strong>备份介质上的副本</strong>同样成立——
    /// 删除线上的行不能让磁带上的那份消失。
    /// </para>
    /// </remarks>
    public string? EncryptionKeyId { get; init; }

    /// <summary>
    /// 本策略作用的实体类型。
    /// </summary>
    public abstract Type EntityType { get; }
}

/// <summary>
/// 针对某个实体类型的保留策略。
/// </summary>
/// <typeparam name="TEntity">受策略约束的实体类型。</typeparam>
/// <example>
/// <code>
/// new RetentionPolicy&lt;Tip&gt;
/// {
///     Name = "tip-statutory-retention",
///     RetentionPeriod = TimeSpan.FromDays(730),
///     Timestamp = t =&gt; t.CreationTime,
///     Scope = t =&gt; t.Status == TipStatus.Closed,   // 只销毁已结案的
///     EncryptionKeyId = "tip-2026"
/// }
/// </code>
/// </example>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public sealed class RetentionPolicy<TEntity> : RetentionPolicy
    where TEntity : class, IEntity
{
    /// <summary>
    /// 从哪个字段判定到期。
    /// </summary>
    /// <remarks>
    /// <strong>选哪个时间字段是一项实质决定</strong>：按创建时间算，保留期从数据产生起计；
    /// 按结案时间算，一个长期挂着的案子会一直不到期。两者都合理，但只有一个符合你的合规口径。
    /// </remarks>
    public required Expression<Func<TEntity, DateTime>> Timestamp { get; init; }

    /// <summary>
    /// 可选的适用范围：只有满足该条件的记录才受本策略约束。
    /// </summary>
    /// <remarks>
    /// 留空表示该实体的全部记录都适用。典型用法是排除尚未结案、
    /// 或被标记为需长期保存的记录。诉讼保全这类<strong>动态</strong>豁免请用
    /// <see cref="ILitigationHoldProvider"/>，不要塞进这里——
    /// 本条件会被翻译成 SQL，问不了外部系统。
    /// </remarks>
    public Expression<Func<TEntity, bool>>? Scope { get; init; }

    /// <inheritdoc />
    public override Type EntityType => typeof(TEntity);
}
