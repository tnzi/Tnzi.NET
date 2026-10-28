using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Tnzi.EFCore.Internal;

/// <summary>
/// 让框架实体的单列 Guid / long 主键由框架的 Id 规则生成（<see cref="IdGenerationHelper.GenerateId"/>），而不是 EF 的默认生成器或数据库。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>Guid</b>：EF 的约定把单列 Guid 主键配成「新增时客户端生成」，<c>DbSet.Add</c> 那一刻就用它自己的生成器填上 Id；
/// <c>SaveChanges</c> 里的自动赋 Id 只在 Id 仍为默认值时动手，对 Guid 主键从来轮不到。于是注册的
/// <c>IEntityIdGenerator</c> 被静默无视，按 provider 选的顺序 Guid 排列也从未生效，只有 <c>NewId()</c> 与
/// Dapper 批量插入两条路用上了框架的规则，同一张表里两种 Id 形态并存。
/// 这里只换生成器、不改 <c>ValueGenerated</c>：生成器是运行期元数据，不进迁移快照，不需要迁移。
/// </para>
/// <para>
/// ★★ <b>long</b>：EF 的约定把单列 long 主键配成数据库生成（SQL Server 的 IDENTITY、PostgreSQL 的 identity 列），
/// 而 <c>SaveChanges</c> 的自动赋 Id 总会给它填一个 Snowflake 值。SQL Server 拒绝向 IDENTITY 列显式插值
/// （IDENTITY_INSERT 为 OFF），这样的实体在 SQL Server 上一行都写不进去；其它库照收显式值，数据库那一侧的自增从来没被用过。
/// 所以 long 主键改为「数据库不生成」（<see cref="ValueGenerated.Never"/>）、由 <see cref="FrameworkSnowflakeValueGenerator"/>
/// 在 <c>Add</c> 时就填上 Id（批量 <c>AddRange</c> 的多个新实体因此不会都以 0 为键而在变更跟踪器里相撞）。
/// 这一条<b>会</b>进迁移快照：消费方要生成一次迁移，SQL Server 上的表须删掉重建（见升级说明）。
/// </para>
/// <para>
/// 跳过显式配置过生成器、关掉了生成、由数据库默认值 / 计算列生成、或显式配置了 provider 值生成策略（如 <c>UseIdentityColumn()</c>）的主键：
/// 那些是使用方有意为之，约定不覆盖。
/// </para>
/// </remarks>
internal sealed class FrameworkKeyConvention : IModelFinalizingConvention
{
    /// <summary>
    /// provider 记值生成策略的注解名后缀（<c>SqlServer:ValueGenerationStrategy</c>、<c>Npgsql:ValueGenerationStrategy</c>、
    /// <c>MySql:ValueGenerationStrategy</c>）。按名字认而不引用 provider 程序集：本包不依赖任何一个 provider。
    /// </summary>
    private const string ValueGenerationStrategyAnnotationSuffix = ":ValueGenerationStrategy";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.BaseType != null || entityType.IsOwned() || !typeof(IEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var key = entityType.FindPrimaryKey();
            if (key is not { Properties.Count: 1 })
            {
                continue;
            }

            var property = key.Properties[0];
            if (property.ValueGenerated != ValueGenerated.OnAdd
                || property.GetValueGeneratorFactory() != null
                || property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql) != null
                || property.FindAnnotation(RelationalAnnotationNames.DefaultValue) != null
                || property.FindAnnotation(RelationalAnnotationNames.ComputedColumnSql) != null)
            {
                continue;
            }

            if (property.ClrType == typeof(Guid))
            {
                property.Builder.HasValueGenerator(typeof(FrameworkGuidValueGenerator));
            }
            else if (property.ClrType == typeof(long))
            {
                ApplySnowflakeKey(property);
            }
        }
    }

    private static void ApplySnowflakeKey(IConventionProperty property)
    {
        var strategyAnnotations = property.GetAnnotations()
            .Where(a => a.Name.EndsWith(ValueGenerationStrategyAnnotationSuffix, StringComparison.Ordinal))
            .ToList();

        // 使用方显式选了数据库那一侧的生成策略：不动它
        if (strategyAnnotations.Any(a => a.GetConfigurationSource() != ConfigurationSource.Convention))
        {
            return;
        }

        // 数据注解（[DatabaseGenerated(Identity)]）或显式 ValueGeneratedOnAdd()：同样是有意为之
        if (property.GetValueGeneratedConfigurationSource() is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation)
        {
            return;
        }

        // ★ 以数据注解的优先级设置，而不是约定：关系型与 provider 的值生成约定在模型定稿时会按「long 主键 = 自增」
        // 重新算一遍 ValueGenerated（同为约定来源就会把这里改回 OnAdd），而它们的定稿约定排在本约定之后。
        // 显式的 Fluent 配置仍高于数据注解，使用方照旧能改回去。
        if (property.Builder.ValueGenerated(ValueGenerated.Never, fromDataAnnotation: true) == null)
        {
            return;
        }

        // provider 的约定可能已在本约定之前把「按约定是 IDENTITY」物化成了注解；不清掉的话迁移照旧建 IDENTITY 列
        foreach (var annotation in strategyAnnotations)
        {
            property.Builder.HasNoAnnotation(annotation.Name);
        }

        property.Builder.HasValueGenerator(typeof(FrameworkSnowflakeValueGenerator));
    }
}

/// <summary>
/// 按框架的 Id 规则生成 Guid：注册了 <see cref="IEntityIdGenerator"/> 就用它，否则按 provider 选顺序 Guid 的排列。
/// </summary>
internal sealed class FrameworkGuidValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry)
        => IdGenerationHelper.GenerateId(entry.Context, entry.Metadata.ClrType, typeof(Guid)) is Guid id && id != Guid.Empty
            ? id
            : IdGenerationHelper.NewSequentialGuid(entry.Context);
}

/// <summary>
/// 按框架的 Id 规则生成 long 主键：注册了 <see cref="IEntityIdGenerator"/> 就用它，否则 Snowflake（<see cref="IdHelper.NextId"/>）。
/// </summary>
/// <remarks>与 <c>SaveChanges</c> 的自动赋 Id 同一套规则；生成器给不出非零值时退回 Snowflake，主键不能是 0。</remarks>
internal sealed class FrameworkSnowflakeValueGenerator : ValueGenerator<long>
{
    public override bool GeneratesTemporaryValues => false;

    public override long Next(EntityEntry entry)
        => IdGenerationHelper.GenerateId(entry.Context, entry.Metadata.ClrType, typeof(long)) is long id && id != 0
            ? id
            : IdHelper.NextId();
}
