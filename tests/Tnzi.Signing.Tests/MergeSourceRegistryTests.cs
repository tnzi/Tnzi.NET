namespace Tnzi.Signing.Tests;

/// <summary>
/// 宿主类型到 provider / sink 的归拢。
/// </summary>
/// <remarks>
/// 这个注册表是整个模块与业务模块之间<b>唯一</b>的接触面：签署模块从不点名任何 provider，
/// 只按 <c>HostEntityType</c> 字符串来找。
/// </remarks>
public class MergeSourceRegistryTests
{
    private sealed class StubProvider(string entityType) : IMergeSourceProvider
    {
        public string EntityType => entityType;
        public IReadOnlyList<MergeFieldDescriptor> Describe() => [];
        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    private sealed class StubSink(string entityType) : IDocumentHostSink
    {
        public string EntityType => entityType;
        public Task AttachAsync(Guid entityId, Guid fileId, string fileName, Guid requestId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void A_registered_provider_is_found_by_its_host_type()
    {
        var registry = new MergeSourceRegistry([new StubProvider("Matter")], []);

        registry.FindProvider("Matter").ShouldNotBeNull();
    }

    [Fact]
    public void Host_type_lookup_is_case_insensitive()
    {
        // 宿主类型名同时来自持久化字符串与消费方配置。让一次大小写差异变成
        // 「合并变量凭空消失」太不值得。
        var registry = new MergeSourceRegistry([new StubProvider("Matter")], []);

        registry.FindProvider("matter").ShouldNotBeNull();
        registry.FindProvider("MATTER").ShouldNotBeNull();
    }

    [Fact]
    public void An_unwired_host_type_returns_null_rather_than_throwing()
    {
        // 不是错误：一个还没接线的宿主类型只是暂时没有变量可合并。
        var registry = new MergeSourceRegistry([new StubProvider("Matter")], []);

        registry.FindProvider("Invoice").ShouldBeNull();
        registry.FindSink("Invoice").ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_standalone_document_has_no_host_type_and_that_is_legal(string? entityType)
    {
        // HostEntityType 为空 = 不绑定任何业务记录的独立文档，完全合法。
        var registry = new MergeSourceRegistry([new StubProvider("Matter")], []);

        registry.FindProvider(entityType).ShouldBeNull();
        registry.FindSink(entityType).ShouldBeNull();
    }

    [Fact]
    public void Providers_and_sinks_are_indexed_independently()
    {
        // 一个宿主类型可以只提供合并变量而不接收归档（例如只读的参照数据），
        // 反之亦然。两张表各自独立。
        var registry = new MergeSourceRegistry([new StubProvider("Matter")], [new StubSink("Staff")]);

        registry.FindProvider("Matter").ShouldNotBeNull();
        registry.FindSink("Matter").ShouldBeNull();
        registry.FindProvider("Staff").ShouldBeNull();
        registry.FindSink("Staff").ShouldNotBeNull();
    }

    [Fact]
    public void KnownHostTypes_is_the_union_of_both_sides_deduplicated()
    {
        // 管理端用它回答「哪些记录可以发起签署」。
        var registry = new MergeSourceRegistry(
            [new StubProvider("Matter"), new StubProvider("Client")],
            [new StubSink("Matter"), new StubSink("Staff")]);

        registry.KnownHostTypes.ShouldBe(["Client", "Matter", "Staff"]);
    }

    [Fact]
    public void A_blank_entity_type_on_a_registration_is_ignored_rather_than_poisoning_the_table()
    {
        // 一个写错的注册不该让整张表多出一个空键条目。
        var registry = new MergeSourceRegistry([new StubProvider("  "), new StubProvider("Matter")], []);

        registry.KnownHostTypes.ShouldBe(["Matter"]);
    }
}
