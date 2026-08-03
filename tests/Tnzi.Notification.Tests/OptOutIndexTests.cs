using Tnzi.Notification.Entities.Configs;

namespace Tnzi.Notification.Tests;

/// <summary>
/// 可空列参与的唯一约束必须拆成两条索引。
/// </summary>
/// <remarks>
/// ★ 各家数据库对唯一索引里的 NULL 判定不同：PostgreSQL / SQLite 认为 NULL 互不相等
/// （同一组值可以插进任意多行），SQL Server 认为 NULL 彼此相等（只许一行）。所以
/// 「(Address, Channel, 可空 Category) 唯一」这一条索引在两种库上表达的是两件事。
///
/// 对这两张表，Category 为 NULL 不是"碰巧没填"，而是最要紧的那个档位：
/// 整渠道退订 / 该渠道的默认偏好。多写一行的后果不是脏数据而是**功能失效** ——
/// OptInAsync 只删它查到的那一行，于是页面说"已恢复订阅"、人却依然收不到邮件。
///
/// 服务层的判重是 read-then-write，两个并发请求都会读到"还没有"，所以最终把关的
/// 必须是数据库。本测试从 EF 模型元数据读索引，无论过滤器怎么写出来都算数。
/// </remarks>
public class OptOutIndexTests
{
    private static IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IMutableIndex> IndexesOf(IEntityRegister configuration)
    {
        var modelBuilder = new ModelBuilder();
        configuration.RegisterTo(modelBuilder);
        return modelBuilder.Model.FindEntityType(configuration.EntityType)!.GetIndexes().ToList();
    }

    [Theory]
    [InlineData(typeof(OptOutConfiguration), "Address,Channel")]
    [InlineData(typeof(PreferenceConfiguration), "UserId,Channel")]
    public void The_null_branch_of_the_unique_constraint_is_covered_by_its_own_index(Type configurationType, string leadingColumns)
    {
        var indexes = IndexesOf((IEntityRegister)Activator.CreateInstance(configurationType)!);
        var unique = indexes.Where(i => i.IsUnique).ToList();

        // 一条管有值的行：Category 参与，过滤器要求它非空。
        var withCategory = unique.SingleOrDefault(i =>
            i.Properties.Any(p => p.Name == "Category")
            && (i.GetFilter() ?? string.Empty).Contains("IS NOT NULL", StringComparison.OrdinalIgnoreCase));
        withCategory.ShouldNotBeNull(
            "the categorised rows need a unique index whose filter excludes NULL, or the NULL rows fall under " +
            "provider-specific NULL semantics.");

        // 另一条管 NULL 那一支：Category 不参与，过滤器要求它为空。
        var nullBranch = unique.SingleOrDefault(i =>
            i.Properties.All(p => p.Name != "Category")
            && (i.GetFilter() ?? string.Empty).Contains("IS NULL", StringComparison.OrdinalIgnoreCase));
        nullBranch.ShouldNotBeNull(
            "NULL means \"all categories\" here and many rows can carry it, so PostgreSQL will not enforce " +
            "uniqueness on it - that branch needs its own filtered index.");

        // 它必须正好覆盖前导列，否则约束的范围就不是我们想要的那个。
        string.Join(",", nullBranch!.Properties.Select(p => p.Name)).ShouldBe(leadingColumns);
    }

    [Fact]
    public void No_unique_index_leaves_a_nullable_column_to_provider_specific_semantics()
    {
        foreach (var configurationType in new[] { typeof(OptOutConfiguration), typeof(PreferenceConfiguration) })
        {
            var indexes = IndexesOf((IEntityRegister)Activator.CreateInstance(configurationType)!);
            foreach (var index in indexes.Where(i => i.IsUnique))
            {
                var filter = index.GetFilter() ?? string.Empty;
                foreach (var nullable in index.Properties.Where(p => p.IsNullable))
                {
                    filter.ShouldContain(
                        nullable.Name,
                        Case.Insensitive,
                        $"{configurationType.Name}: the unique index over " +
                        $"({string.Join(",", index.Properties.Select(p => p.Name))}) includes the nullable column " +
                        $"'{nullable.Name}' without saying anything about NULL in its filter.");
                }
            }
        }
    }
}
