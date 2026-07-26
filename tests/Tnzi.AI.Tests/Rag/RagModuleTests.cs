using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// AIRagModule 单元测试 - 验证模块属性和配置
/// </summary>
public class RagModuleTests
{
    [Fact]
    public void LoadOrder_Is55()
    {
        var module = new AIRagModule();

        module.LoadOrder.ShouldBe(55);
    }

    [Fact]
    public void TableNamePrefix_IsRAG()
    {
        var module = new AIRagModule();

        module.TableNamePrefix.ShouldBe("RAG");
    }

    [Fact]
    public void DependsOn_AIModule()
    {
        var dependsOnAttrs = typeof(AIRagModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: true)
            .Cast<DependsOnAttribute>()
            .ToList();

        dependsOnAttrs.ShouldNotBeEmpty();
        dependsOnAttrs.SelectMany(a => a.DependedModuleTypes).ShouldContain(typeof(AI.AIModule));
    }

    #region Hybrid search keyword provider selection (C11)

    [Fact]
    public void Hybrid_PostgreSQL_RegistersPgFullTextSearchProvider()
    {
        var services = ConfigureModule(vectorStoreProvider: "PostgreSQL", hybridEnabled: true);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKeywordSearchProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgFullTextSearchProvider));
    }

    [Fact]
    public void Hybrid_InMemory_RegistersInMemoryKeywordSearchProvider()
    {
        var services = ConfigureModule(vectorStoreProvider: "InMemory", hybridEnabled: true);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKeywordSearchProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(InMemoryKeywordSearchProvider));
    }

    [Fact]
    public void Hybrid_Auto_RegistersPgFullTextSearchProvider()
    {
        // "Auto" 对应 PgVectorStore，关键词也应走 PG 全文搜索（与 IVectorStore switch 一致）
        var services = ConfigureModule(vectorStoreProvider: "Auto", hybridEnabled: true);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKeywordSearchProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgFullTextSearchProvider));
    }

    [Fact]
    public void Hybrid_Disabled_DoesNotRegisterKeywordSearchProvider()
    {
        var services = ConfigureModule(vectorStoreProvider: "PostgreSQL", hybridEnabled: false);

        services.Any(d => d.ServiceType == typeof(IKeywordSearchProvider)).ShouldBeFalse();
    }

    #endregion

    #region IVectorStore backend selection (Auto must override AIModule NoOp)

    [Fact]
    public void Auto_OverridesAIModuleNoOpVectorStore_WithPgVectorStore()
    {
        // 生产顺序：AIRagModule.Configure 先注册 PgVectorStore，AIModule 在其后的
        // PostConfigure 阶段才 TryAdd NoOpVectorStore 回退 —— 该回退对已注册的 Pg 天然让位。
        // "Auto"（默认）注册的 Pg 必须胜出，否则向量检索静默返空。
        var services = ConfigureModule(
            vectorStoreProvider: "Auto",
            hybridEnabled: false,
            postRegister: s => s.TryAddScoped<IVectorStore, NoOpVectorStore>());

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgVectorStore));
    }

    [Fact]
    public void Default_WhenProviderUnset_OverridesNoOpVectorStore_WithPgVectorStore()
    {
        // 不配置 VectorStoreProvider 时走默认("Auto")分支，行为应与显式 "Auto" 一致：
        // Configure 注册的 Pg 胜过随后 PostConfigure 才 TryAdd 的 NoOp 回退。
        var services = ConfigureModule(
            vectorStoreProvider: null,
            hybridEnabled: false,
            postRegister: s => s.TryAddScoped<IVectorStore, NoOpVectorStore>());

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgVectorStore));
    }

    [Fact]
    public void Auto_NoExistingRegistration_RegistersPgVectorStore()
    {
        // 即便没有任何既有 IVectorStore 注册（无 NoOp 占位），"Auto" 也应注册 PgVectorStore。
        var services = ConfigureModule(vectorStoreProvider: "Auto", hybridEnabled: false);

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgVectorStore));
    }

    [Fact]
    public void Auto_PreservesUserRegisteredCustomVectorStore()
    {
        // 用户预注册了真实自定义实现（非 INoOpService），"Auto" 必须尊重它、不覆盖。
        var services = ConfigureModule(
            vectorStoreProvider: "Auto",
            hybridEnabled: false,
            preRegister: s => s.AddSingleton<IVectorStore, CustomVectorStore>());

        services.Any(d => d.ServiceType == typeof(IVectorStore)
            && d.ImplementationType == typeof(PgVectorStore)).ShouldBeFalse();

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));
        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(CustomVectorStore));
    }

    [Fact]
    public void Explicit_PostgreSQL_RegistersPgVectorStore()
    {
        var services = ConfigureModule(vectorStoreProvider: "PostgreSQL", hybridEnabled: false);

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(PgVectorStore));
    }

    [Fact]
    public void Explicit_InMemory_RegistersInMemoryVectorStore()
    {
        var services = ConfigureModule(vectorStoreProvider: "InMemory", hybridEnabled: false);

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IVectorStore));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(InMemoryVectorStore));
    }

    /// <summary>
    /// 用户自定义的真实 IVectorStore 实现（不实现 INoOpService），用于验证 "Auto" 不覆盖它。
    /// </summary>
    private sealed class CustomVectorStore : IVectorStore
    {
        public Task<List<VectorSearchResult>> SearchAsync(
            float[] queryVector, int topK, Guid? knowledgeBaseId = null, CancellationToken ct = default)
            => Task.FromResult(new List<VectorSearchResult>());

        public Task UpsertAsync(Guid chunkId, float[] embedding, Guid documentId, Guid knowledgeBaseId,
            string content, int chunkIndex, string? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// 配置 AIRagModule 并返回 ServiceCollection，模拟 PreConfigure + Configure 生命周期。
    /// <paramref name="preRegister"/> 用于在 AIRagModule 配置前模拟更早 Configure 的模块（或用户）已有的注册。
    /// <paramref name="postRegister"/> 用于在 AIRagModule 配置后模拟 AIModule 在 PostConfigure 阶段
    /// TryAdd 的 NoOp 回退（子模块 Configure 相注册先于它，故此回退天然让位）。
    /// </summary>
    private static ServiceCollection ConfigureModule(
        string? vectorStoreProvider,
        bool hybridEnabled,
        Action<IServiceCollection>? preRegister = null,
        Action<IServiceCollection>? postRegister = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        preRegister?.Invoke(services);

        var settings = new Dictionary<string, string?>
        {
            ["AI:Rag:HybridSearch:Enabled"] = hybridEnabled ? "true" : "false",
        };
        if (vectorStoreProvider is not null)
        {
            settings["AI:Rag:VectorStoreProvider"] = vectorStoreProvider;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        services.AddOptions<AIRagOptions>().Bind(configuration.GetSection("AI:Rag"));

        var module = new AIRagModule();
        var context = new ServiceConfigurationContext(services, configuration);
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        postRegister?.Invoke(services);

        return services;
    }
}
