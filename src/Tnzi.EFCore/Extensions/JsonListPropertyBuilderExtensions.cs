namespace Tnzi.EFCore.Extensions;

/// <summary>
/// 把 <see cref="List{T}"/> 属性存成一列 JSON 文本，并让变更跟踪按元素比较。
/// </summary>
/// <remarks>
/// <para>
/// 用法（在 <see cref="EntityTypeConfigurationBase{TEntity, TKey}"/> 的 Configure 里）：
/// <code>
/// builder.Property(e => e.Roles)
///        .HasJsonListConversion()
///        .HasMaxLength(2000);
/// </code>
/// </para>
/// <para>
/// 只写 <c>HasConversion</c> 不够。没有值比较器的转换属性，EF 按引用比较新旧值，
/// <c>agent.Roles.Add("x")</c> 这样的就地修改不会被发现，<c>SaveChanges</c> 什么都不写
/// （EF 会在模型校验时为每个这样的属性记一条警告）。这里一并配上按元素比较的值比较器，
/// 快照存一份拷贝，就地修改与整体替换都能被检测到。
/// </para>
/// </remarks>
public static class JsonListPropertyBuilderExtensions
{
    /// <summary>
    /// 以 <see cref="TnziJsonDefaults.Options"/> 序列化为 JSON 文本存储，<c>null</c> 存为 <c>NULL</c>，
    /// 并配置按元素（顺序敏感）比较的值比较器。
    /// </summary>
    /// <typeparam name="T">元素类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    public static PropertyBuilder<List<T>?> HasJsonListConversion<T>(this PropertyBuilder<List<T>?> builder)
    {
        Check.NotNull(builder);

        return builder.HasConversion(
            v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
            v => v == null ? null : JsonSerializer.Deserialize<List<T>>(v, TnziJsonDefaults.Options),
            new ValueComparer<List<T>?>(
                (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                v => v == null ? 0 : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                v => v == null ? null : v.ToList()));
    }
}
