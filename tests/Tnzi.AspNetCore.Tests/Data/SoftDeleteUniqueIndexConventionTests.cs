using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tnzi.Domain.Entities;
using Tnzi.EFCore;

namespace Tnzi.AspNetCore.Tests.Data;

/// <summary>
/// 反射约定门禁:软删实体上的**唯一索引必须带 `IsDeleted = false` 过滤器**。
///
/// 为什么值得一道门禁:软删只是把行标记为已删,物理行仍在表里;而全局查询过滤器让
/// 应用侧**看不见**那些幽灵行。于是"查重查不到 → 放行 → INSERT → 撞数据库唯一约束"
/// 成为必然路径,报出来的还是不透明的 500 而不是 409。
///
/// 这颗雷本仓库炸过:2026-07-22 `AuthToken` 的 2FA 登录 500 就是它
/// (见 src/Tnzi.Identity/CLAUDE.md 同日条目,当时的结论原文写着"必然复发")。
/// 那次只修了 `AuthToken` 一个实体,没有人回头问"还有哪些软删实体挂着无过滤唯一索引" ——
/// 答案是 `User` / `UserDetail` / `UserQuota`,全在最核心的模块里。
/// 本测试就是那个"回头问一遍"的机器版本,让第三次不再发生。
///
/// 两条合规路径,任选其一:
///   1. 索引加 <c>.HasFilter(IndexFilterFactory.GetIsDeletedFalse())</c>(保留软删);
///   2. 实体去掉软删(基类 <c>FullAuditedEntity</c> → <c>AuditedEntity</c>),
///      像 `AuthToken` 那样 —— 短暂凭证类实体没有审计留痕需求,硬删更简单。
///
/// 程序集来源与 <c>AdminWriteEndpointPermissionConventionTests</c> 一致:测试输出目录里
/// 的全部 <c>Tnzi*.dll</c>(本项目经 Tnzi.Hosting 传递引用全部业务模块)。
/// </summary>
public class SoftDeleteUniqueIndexConventionTests
{
    /// <summary>
    /// 存量豁免。键格式:<c>{Entity.FullName}:{逗号分隔的属性名}</c>。
    /// 加入前必须写清楚为什么这个唯一索引不需要过滤器 —— 通常只有
    /// "该实体其实不会被软删" 这一种理由,而那说明它不该实现 ISoftDelete。
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // 目前为空:本轮已把扫出的存量违规全部修掉,而不是豁免掉。
    };

    [Fact]
    public void SoftDeleteEntities_UniqueIndexes_MustFilterOutDeletedRows()
    {
        var configurations = DiscoverSoftDeleteConfigurations();

        // 空洞守卫:配置发现一旦失效(程序集没复制过来、基类改名),这个测试会静静地
        // 变成"扫了 0 个实体所以通过",比没有测试更糟。
        Assert.True(
            configurations.Count >= 20,
            $"Only {configurations.Count} soft-delete entity configurations were discovered; " +
            "the reflection scan is probably broken rather than the codebase being clean.");

        var violations = new List<string>();

        foreach (var (configurationType, entityType) in configurations)
        {
            IReadOnlyList<IMutableIndex> indexes;
            try
            {
                indexes = BuildIndexes(configurationType);
            }
            catch (Exception ex)
            {
                // 配置需要本测试装配不出来的依赖时跳过,并记下来 —— 不要伪装成通过。
                violations.Add($"{entityType.FullName}: configuration could not be built ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            foreach (var index in indexes)
            {
                if (!index.IsUnique)
                    continue;

                var properties = string.Join(",", index.Properties.Select(p => p.Name));
                if (Allowlist.Contains($"{entityType.FullName}:{properties}"))
                    continue;

                var filter = index.GetFilter();
                if (string.IsNullOrWhiteSpace(filter) || !filter.Contains("IsDeleted", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{entityType.FullName} unique index ({properties}) has no IsDeleted filter " +
                        $"(current filter: {filter ?? "<none>"})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Soft-delete entities must not carry unfiltered unique indexes. " +
            "A soft-deleted row stays in the table, so the database still enforces the constraint " +
            "while the global query filter hides the row from the duplicate check.\n  " +
            string.Join("\n  ", violations.OrderBy(v => v, StringComparer.Ordinal)));
    }

    /// <summary>
    /// 把单个实体配置注册进一个干净的 <see cref="ModelBuilder"/>,取回它声明的索引。
    /// 直接读 EF 的模型元数据,而不是正则扫源码 —— 这样 `HasFilter` 无论经工厂、常量
    /// 还是字面量写出来都算数。
    /// </summary>
    private static IReadOnlyList<IMutableIndex> BuildIndexes(Type configurationType)
    {
        var modelBuilder = new ModelBuilder();
        var configuration = (IEntityRegister)Activator.CreateInstance(configurationType)!;
        configuration.RegisterTo(modelBuilder);

        var entityType = modelBuilder.Model.FindEntityType(configuration.EntityType);
        return entityType?.GetIndexes().ToList() ?? [];
    }

    private static List<(Type Configuration, Type Entity)> DiscoverSoftDeleteConfigurations()
    {
        var found = new List<(Type, Type)>();

        foreach (var assembly in LoadTnziAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;
                if (!typeof(IEntityRegister).IsAssignableFrom(type))
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                var entityType = ResolveEntityType(type);
                if (entityType == null || !typeof(ISoftDelete).IsAssignableFrom(entityType))
                    continue;

                found.Add((type, entityType));
            }
        }

        return found;
    }

    /// <summary>
    /// 从 <c>EntityTypeConfigurationBase&lt;TEntity, TKey&gt;</c> 的基类链里取出 TEntity。
    /// </summary>
    private static Type? ResolveEntityType(Type configurationType)
    {
        for (var current = configurationType.BaseType; current != null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(EntityTypeConfigurationBase<,>))
            {
                return current.GetGenericArguments()[0];
            }
        }
        return null;
    }

    private static IEnumerable<Assembly> LoadTnziAssemblies()
    {
        var directory = Path.GetDirectoryName(typeof(SoftDeleteUniqueIndexConventionTests).Assembly.Location)!;
        foreach (var path in Directory.GetFiles(directory, "Tnzi*.dll"))
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch
            {
                continue;
            }
            yield return assembly;
        }
    }
}
