using System.Reflection;
using Microsoft.Data.Sqlite;
using Tnzi.AI.Channels.Entities;
using Tnzi.Domain.Entities;

namespace Tnzi.AI.Tests.Architecture;

/// <summary>
/// 架构测试：强制实体策略约束，防止"C1 类 bug"（IndexFilter 引用不存在的列）
/// 以及"IScopedResource 误实现 IMultiTenant"进入代码库。
/// </summary>
public class EntityPolicyTests : IDisposable
{
    // =====================================================================
    // DbContext 基础设施（复用 AiIntegrationDbContext 的配置模式）
    // =====================================================================

    private readonly SqliteConnection _connection;
    private readonly EntityPolicyDbContext _context;

    public EntityPolicyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<EntityPolicyDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new EntityPolicyDbContext(options, currentUserMock.Object);
        // EnsureCreated 执行 DDL → 在 SQLite 上会验证过滤器列是否真实存在（软断言）。
        // 即使不 EnsureCreated，context.Model 也能提供完整的元模型用于检查。
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // =====================================================================
    // Assertion A - IScopedResource 与 IMultiTenant 互斥
    // =====================================================================

    /// <summary>
    /// A1: Provider 必须实现 IScopedResource，且不实现 IMultiTenant。
    /// </summary>
    [Fact]
    public void ScopedResourceEntities_ImplementIScopedResource_And_DoNotImplementIMultiTenant()
    {
        // Provider: FullAuditedEntity (有 IsDeleted) + IScopedResource
        typeof(Provider).Should_ImplementIScopedResource();
        typeof(Provider).Should_NotImplementIMultiTenant();
    }

    /// <summary>
    /// A2: 扫描全部 AI 程序集（核心 + 6 个子模块），所有实现 IScopedResource 的类型都不能
    /// 同时实现 IMultiTenant。这是防止"误加 IMultiTenant"的持续守卫，覆盖跨程序集的共享资源。
    /// </summary>
    [Fact]
    public void AllIScopedResourceTypes_DoNotImplementIMultiTenant()
    {
        var scopedResourceTypes = AllAiAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IScopedResource).IsAssignableFrom(t))
            .ToList();

        // 必须有已知的 scoped resource 类型（至少 Provider + SkillEntity）
        scopedResourceTypes.ShouldNotBeEmpty("There should be at least one IScopedResource implementation");

        // 证明扫描穿透到子程序集：SkillEntity 位于 Tnzi.AI.Skills，必须被发现。
        scopedResourceTypes.ShouldContain(typeof(SkillEntity),
            "AllIScopedResourceTypes_DoNotImplementIMultiTenant must reach sub-assemblies - " +
            "SkillEntity (Tnzi.AI.Skills) implements IScopedResource and should be discovered. " +
            "If it is missing, the assembly anchor set is incomplete or SkillEntity lost its marker.");

        var violators = scopedResourceTypes
            .Where(t => typeof(IMultiTenant).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        violators.ShouldBeEmpty(
            $"The following types implement both IScopedResource and IMultiTenant (mutually exclusive): " +
            $"{string.Join(", ", violators)}. " +
            "IScopedResource entities must NOT implement IMultiTenant - the global query filter hides System rows (TenantId=null) from all real tenants.");
    }

    // =====================================================================
    // Assertion C - 全量实体分类表（锁定多租户审计结果，防止 MT/软删除/Scope 漂移）
    // =====================================================================

    /// <summary>
    /// 全部 AI 程序集的锚点集合（去重）。任何新增的 AI 子模块若引入实体，必须在此登记锚点。
    /// </summary>
    private static readonly Assembly[] AllAiAssemblies =
    [
        typeof(Provider).Assembly,                 // Tnzi.AI (core)
        typeof(SkillEntity).Assembly,              // Tnzi.AI.Skills
        typeof(SessionBindingRule).Assembly,       // Tnzi.AI.Channels
        typeof(KnowledgeBase).Assembly,            // Tnzi.AI.Rag
        typeof(WorkflowDefinition).Assembly,       // Tnzi.AI.Workflow
        typeof(McpToolUsageRecord).Assembly,       // Tnzi.AI.Mcp
    ];

    /// <summary>
    /// 审计分类表（事实来源）：32 个实体 → (IsMultiTenant, IsSoftDelete, IsSharedResource)。
    /// <para>
    /// SharedResource(SR) 精确等价于"实现 IScopedResource"——经过 SkillEntity 标记后，
    /// 两个共享资源 (Provider/SkillEntity) 全部实现该标记接口，于是不变式很清晰：
    /// SR 实体 == IScopedResource 实现集，且它们一律 NOT IMultiTenant。
    /// </para>
    /// <para>
    /// 这张表把多租户审计固化为门禁：任何实体改了基类 / 加了 IMultiTenant / 加了 IScopedResource，
    /// 都会让对应行失配从而 CI 失败；新增未登记实体也会失败并提示开发者补登。
    /// </para>
    /// </summary>
    private static readonly Dictionary<Type, (bool MultiTenant, bool SoftDelete, bool SharedResource)> ExpectedClassification = new()
    {
        // ---- Tnzi.AI (core) ----
        [typeof(Agent)] = (true, true, false),
        [typeof(AgentVersion)] = (true, false, false),
        [typeof(AgentTask)] = (true, false, false),
        [typeof(AgentArtifact)] = (true, false, false),
        [typeof(AgentThread)] = (true, true, false),
        [typeof(AgentThreadMessage)] = (true, false, false),
        [typeof(AgentRun)] = (true, true, false),
        [typeof(AgentRunNode)] = (true, false, false),
        [typeof(AgentRunTrace)] = (true, false, false),
        [typeof(EvaluationRun)] = (true, false, false),
        [typeof(MemoryEntry)] = (true, false, false),
        [typeof(EntityMemory)] = (true, false, false),
        [typeof(UsageLog)] = (true, false, false),
        [typeof(UserQuota)] = (true, true, false),
        [typeof(UserProfile)] = (true, false, false),
        [typeof(SubAgentType)] = (true, false, false),
        [typeof(ToolPermissionRuleEntity)] = (true, false, false),
        [typeof(Provider)] = (false, true, true),
        // ---- Tnzi.AI (core) - Agent resource grant junctions ----
        // 租户级软删除结合实体，FK 仅指向同程序集的 Agent。资源（工具组/技能/知识库）
        // 按值引用，故 MT=true, SD=true, SR=false（不是共享资源，不实现 IScopedResource）。
        [typeof(AgentToolGrant)] = (true, true, false),
        [typeof(AgentSkillGrant)] = (true, true, false),
        [typeof(AgentKnowledgeGrant)] = (true, true, false),
        // ---- Tnzi.AI.Skills ----
        [typeof(SkillEntity)] = (false, true, true),
        [typeof(SkillCategory)] = (true, false, false),
        // ---- Tnzi.AI.Rag ----
        [typeof(KnowledgeBase)] = (true, true, false),
        [typeof(KnowledgeDocument)] = (true, false, false),
        [typeof(DocumentChunk)] = (true, false, false),
        [typeof(KnowledgeGraphNode)] = (true, false, false),
        [typeof(KnowledgeGraphEdge)] = (true, false, false),
        // ---- Tnzi.AI.Workflow ----
        [typeof(WorkflowDefinition)] = (true, true, false),
        [typeof(WorkflowDefinitionVersion)] = (true, false, false),
        [typeof(WorkflowExecution)] = (true, false, false),
        // ---- Tnzi.AI.Mcp ----
        // TenantId 是分析维度字段（非隔离边界），实体刻意不实现 IMultiTenant。
        [typeof(McpToolUsageRecord)] = (false, false, false),
        // ---- McpServerRegistration (MCP Client 管理面, Tnzi.AI core) ----
        [typeof(McpServerRegistration)] = (true, true, false),
        // ---- Tnzi.AI.Channels ----
        [typeof(SessionBindingRule)] = (true, false, false),
        [typeof(ChannelThreadMapping)] = (true, false, false),
    };

    /// <summary>
    /// C1: 反射发现所有 AI 程序集中的具体实体（命名空间含 ".Entities" 的 IEntity 实现），
    /// 逐个比对 IMultiTenant / ISoftDelete / IScopedResource 三个维度与审计表是否一致。
    /// <list type="bullet">
    /// <item>新增未登记的实体 → 失败并要求开发者分类登记。</item>
    /// <item>实体的 MT / 软删除 / Scope 维度与审计表漂移 → 失败。</item>
    /// <item>审计表存在陈旧条目（无对应实体）→ 失败。</item>
    /// </list>
    /// </summary>
    [Fact]
    public void AllAiEntities_MatchDeclaredClassification()
    {
        var discovered = AllAiAssemblies
            .SelectMany(SafeGetTypes)
            .Where(IsConcreteAiEntity)
            .Distinct()
            .ToList();

        discovered.ShouldNotBeEmpty("No AI entity types were discovered - the assembly anchor set is broken.");

        var mismatches = new List<string>();

        // (a) 每个发现的实体都必须登记，且三维度匹配。
        foreach (var type in discovered)
        {
            if (!ExpectedClassification.TryGetValue(type, out var expected))
            {
                mismatches.Add(
                    $"Unclassified entity '{type.FullName}'. " +
                    "Add it to EntityPolicyTests.ExpectedClassification with its " +
                    "(IsMultiTenant, IsSoftDelete, IsSharedResource) values per the multi-tenant audit.");
                continue;
            }

            var actualMt = typeof(IMultiTenant).IsAssignableFrom(type);
            var actualSd = typeof(ISoftDelete).IsAssignableFrom(type);
            var actualSr = typeof(IScopedResource).IsAssignableFrom(type);

            if (actualMt != expected.MultiTenant || actualSd != expected.SoftDelete || actualSr != expected.SharedResource)
            {
                mismatches.Add(
                    $"Entity '{type.Name}' classification drift - " +
                    $"expected (MT={expected.MultiTenant}, SD={expected.SoftDelete}, SR={expected.SharedResource}) " +
                    $"but live type is (MT={actualMt}, SD={actualSd}, SR={actualSr}).");
            }
        }

        // (b) 审计表里不能有陈旧条目（对应实体已删除/改名）。
        var discoveredSet = discovered.ToHashSet();
        foreach (var key in ExpectedClassification.Keys)
        {
            if (!discoveredSet.Contains(key))
            {
                mismatches.Add(
                    $"Stale classification entry '{key.FullName}' - no matching discovered entity. " +
                    "Remove it from ExpectedClassification or fix the type reference.");
            }
        }

        mismatches.ShouldBeEmpty(
            $"AI entity classification table is out of sync with live types ({mismatches.Count} issue(s)):" +
            Environment.NewLine +
            string.Join(Environment.NewLine, mismatches.Select(m => $"  - {m}")));

        // 守卫：发现的实体数应与审计表条目数一致（防止两侧同时漂移而互相抵消）。
        discovered.Count.ShouldBe(ExpectedClassification.Count,
            $"Discovered {discovered.Count} AI entities but classification table has {ExpectedClassification.Count} entries.");
    }

    // =====================================================================
    // 反射辅助
    // =====================================================================

    /// <summary>
    /// 具体 AI 实体判定：非抽象 class + 实现 IEntity + 命名空间含 ".Entities"。
    /// 命名空间过滤把测试桩 / 基础设施类型排除在外，只保留持久化实体。
    /// </summary>
    private static bool IsConcreteAiEntity(Type t)
    {
        return t is { IsClass: true, IsAbstract: false }
               && typeof(IEntity).IsAssignableFrom(t)
               && t.Namespace is { } ns
               && ns.Contains(".Entities", StringComparison.Ordinal);
    }

    /// <summary>
    /// 安全获取程序集类型（容忍 ReflectionTypeLoadException，仅取可加载部分）。
    /// </summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // =====================================================================
    // Assertion B - IsDeleted 过滤索引要求实体实现 ISoftDelete (C1 捕获器)
    // =====================================================================

    /// <summary>
    /// B1: EF 模型中，任何带有 "IsDeleted" 过滤条件的索引，其对应实体必须实现 ISoftDelete。
    /// 这是防止过滤索引误用 IndexFilterFactory.GetIsDeletedFalse 导致 DDL 崩溃的持续守卫。
    /// </summary>
    [Fact]
    public void AllIndexesWithIsDeletedFilter_RequireISoftDeleteOnEntity()
    {
        var violations = new List<string>();

        foreach (var entityType in _context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var implementsSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);

            foreach (var index in entityType.GetIndexes())
            {
                var filter = index.GetFilter();
                if (filter == null) continue;

                // 判断过滤条件是否涉及 IsDeleted 列（大小写不敏感）
                if (!filter.Contains("IsDeleted", StringComparison.OrdinalIgnoreCase)) continue;

                if (!implementsSoftDelete)
                {
                    var indexColumns = string.Join(", ",
                        index.Properties.Select(p => p.Name));
                    violations.Add(
                        $"Entity '{clrType.Name}' has index filter '{filter}' referencing 'IsDeleted', " +
                        $"but '{clrType.Name}' does not implement ISoftDelete (no IsDeleted column). " +
                        $"Index columns: [{indexColumns}]. " +
                        $"Fix: remove the filter, or change the entity base class to FullAuditedEntity.");
                }
            }
        }

        violations.ShouldBeEmpty(
            $"Found {violations.Count} index filter violation(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v}")));
    }
}

// =====================================================================
// 测试专用 DbContext - 注册所有 AI 核心实体（同 AiIntegrationDbContext）
// =====================================================================

/// <summary>
/// 架构测试专用 DbContext，覆盖 Provider 等主 AI 核心实体集。
/// </summary>
internal sealed class EntityPolicyDbContext : TnziDbContext<EntityPolicyDbContext>
{
    public EntityPolicyDbContext(
        DbContextOptions<EntityPolicyDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 注册所有 AI 核心实体配置（覆盖 Provider 及其他核心实体）
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunNodeConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunTraceConfiguration());
        modelBuilder.ApplyConfiguration(new AgentVersionConfiguration());
        modelBuilder.ApplyConfiguration(new UsageLogConfiguration());
        modelBuilder.ApplyConfiguration(new UserQuotaConfiguration());
        modelBuilder.ApplyConfiguration(new MemoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new EntityMemoryConfiguration());
        modelBuilder.ApplyConfiguration(new UserProfileConfiguration());
        modelBuilder.ApplyConfiguration(new AgentArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new EvaluationRunConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new SubAgentTypeConfiguration());
        modelBuilder.ApplyConfiguration(new ToolPermissionRuleEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AgentTaskConfiguration());
        modelBuilder.ApplyConfiguration(new AgentToolGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentSkillGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentKnowledgeGrantConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

// =====================================================================
// 本地断言扩展（避免污染全局；仅供此文件使用）
// =====================================================================

file static class TypeAssertionExtensions
{
    internal static void Should_ImplementIScopedResource(this Type type)
    {
        typeof(IScopedResource).IsAssignableFrom(type)
            .ShouldBeTrue(
                $"Expected '{type.Name}' to implement IScopedResource, but it does not. " +
                "Add ', IScopedResource' to the class declaration.");
    }

    internal static void Should_NotImplementIMultiTenant(this Type type)
    {
        typeof(IMultiTenant).IsAssignableFrom(type)
            .ShouldBeFalse(
                $"Expected '{type.Name}' NOT to implement IMultiTenant (it uses IScopedResource pattern instead), " +
                "but it does. Remove IMultiTenant - the global query filter hides System rows from all real tenants.");
    }
}
