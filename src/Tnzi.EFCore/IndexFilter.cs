namespace Tnzi.EFCore;

/// <summary>
/// 部分索引过滤条件的可组合构造器：把谓词拼起来，到 <see cref="Build()"/> 那一刻才按数据库提供者出 SQL。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IndexFilterFactory"/> 用「一个组合一个方法」的形状覆盖常见情形，于是它的方法数是
/// 「谓词 × 要不要叠 IsDeleted × 传不传 provider」的笛卡尔积 —— 二十多个，且每来一个新谓词就要再加一对。
/// 真正的代价不是方法多，是<b>组合不出来的那一格没有出路</b>：消费方遇到「列不为空串，且未软删」时
/// 工厂里没有对应方法，于是退回手拼字符串
/// <c>$"{GetIsDeletedFalse()} AND \"TokenHash\" &lt;&gt; ''"</c> —— 那对双引号是 PostgreSQL 的语法，
/// 而躲开硬编码正是这个工厂存在的全部理由。实测发生过。
/// </para>
/// <para>
/// 本类型按<b>谓词</b>而不是按组合来组织，因此新增一个谓词是加一个方法而不是加一族：
/// <code>
/// builder.HasIndex(r => r.TokenHash).IsUnique()
///     .HasFilter(IndexFilter.NotEmpty("TokenHash").AndNotDeleted().Build());
///
/// builder.HasIndex(o => o.Status)
///     .HasFilter(IndexFilter.EqualTo("Status", 0).AndNotDeleted().Build());
///
/// builder.HasIndex(a => new { a.PartyId, a.IsDefault }).IsUnique()
///     .HasFilter(IndexFilter.IsTrue("IsDefault").AndNotDeleted().Build());
/// </code>
/// </para>
/// <para>
/// <see cref="IndexFilterFactory"/> 的既有方法一个不动，也不打算退役：
/// <c>GetIsDeletedFalse()</c> 这类单谓词调用比 <c>IndexFilter.NotDeleted().Build()</c> 短，
/// 现有几百处调用点没有理由改写。本类型要顶替的是<b>手拼字符串</b>那条路。
/// </para>
/// <para>
/// ★ 不可变：每个方法返回新实例，链上的中间值可以安全复用。
/// </para>
/// </remarks>
public sealed class IndexFilter
{
    private readonly IReadOnlyList<Func<DatabaseProvider, string>> _terms;

    private IndexFilter(IReadOnlyList<Func<DatabaseProvider, string>> terms) => _terms = terms;

    private static IndexFilter From(Func<DatabaseProvider, string> term) =>
        new([term]);

    // ── 谓词 ────────────────────────────────────────────────────────────

    /// <summary>未软删除（<c>IsDeleted = false</c>，布尔字面量按库而异）。</summary>
    public static IndexFilter NotDeleted() =>
        From(p => IndexFilterFactory.GetIsDeletedFalse(p));

    /// <summary>列不为 NULL。</summary>
    public static IndexFilter NotNull(string columnName)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => IndexFilterFactory.GetColumnNotNull(columnName, p));
    }

    /// <summary>列为 NULL。与 <see cref="NotNull"/> 配对，把可空列参与的唯一约束拆成两条索引。</summary>
    public static IndexFilter Null(string columnName)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => IndexFilterFactory.GetColumnNull(columnName, p));
    }

    /// <summary>
    /// 列不是空串（<c>&lt;&gt; ''</c>）。
    /// </summary>
    /// <remarks>
    /// ★ 这一条正是工厂里缺的那一格，也是本类型的直接动因。
    /// <para>
    /// <b>它顺带排除 NULL</b>，不必再叠 <see cref="NotNull"/>：SQL 里 <c>NULL &lt;&gt; ''</c> 的结果是
    /// NULL 而不是真，而部分索引只收谓词为真的行。想显式表达「有值且非空」时叠一个也无害，只是冗余。
    /// </para>
    /// </remarks>
    public static IndexFilter NotEmpty(string columnName)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => $"{IndexFilterFactory.QuoteIdentifier(columnName, p)} <> ''");
    }

    /// <summary>列等于某个整数常量（枚举一般按底层值比较）。</summary>
    public static IndexFilter EqualTo(string columnName, int value)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => $"{IndexFilterFactory.QuoteIdentifier(columnName, p)} = {value}");
    }

    /// <summary>列不等于某个整数常量。</summary>
    public static IndexFilter NotEqualTo(string columnName, int value)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => IndexFilterFactory.GetColumnNotEquals(columnName, value, p));
    }

    /// <summary>布尔列为真（字面量按库而异：<c>= 1</c> / <c>= true</c> / <c>= TRUE</c>）。</summary>
    public static IndexFilter IsTrue(string columnName)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return From(p => IndexFilterFactory.GetColumnTrue(columnName, p));
    }

    /// <summary>
    /// 原样的 SQL 片段，供本类型尚未覆盖的谓词使用。
    /// </summary>
    /// <remarks>
    /// ★ <b>逃生舱口，不是常规入口。</b>片段里的标识符引用与字面量语法由调用方负责跨库正确 ——
    /// 也就是说它把这个类型要解决的问题原样交还给你。用它之前先问一句该谓词是不是值得加成一个方法：
    /// 多半是值得的，因为下一个人会遇到同一格。列名请经
    /// <see cref="IndexFilterFactory.QuoteIdentifier(string, DatabaseProvider)"/> 引用。
    /// </remarks>
    public static IndexFilter Raw(Func<DatabaseProvider, string> sql)
    {
        Check.NotNull(sql);
        return From(sql);
    }

    // ── 组合 ────────────────────────────────────────────────────────────

    /// <summary>与另一个条件取交集。</summary>
    public IndexFilter And(IndexFilter other)
    {
        Check.NotNull(other);
        return new IndexFilter([.. _terms, .. other._terms]);
    }

    /// <summary>叠上「未软删除」，即 <c>And(NotDeleted())</c>。软删除表上最常见的那一项。</summary>
    public IndexFilter AndNotDeleted() => And(NotDeleted());

    // ── 出 SQL ──────────────────────────────────────────────────────────

    /// <summary>按指定数据库提供者渲染成 <c>HasFilter</c> 可用的 SQL。</summary>
    public string Build(DatabaseProvider provider) =>
        string.Join(" AND ", _terms.Select(term => term(provider)));

    /// <summary>
    /// 按<b>当前</b>数据库提供者渲染（实体配置期的环境值，与工厂的无参重载同一来源）。
    /// </summary>
    public string Build() =>
        Build(EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault());

    /// <inheritdoc cref="Build()"/>
    public override string ToString() => Build();
}
