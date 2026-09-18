namespace Tnzi.AI.Tests.Agents;

/// <summary>
/// <see cref="SubAgentRegistry"/> 是全局单例，而 <see cref="SubAgentType"/> 是按租户存放的（唯一索引 TenantId+Name）。
/// 注册表必须按租户分桶：任一租户管理员 CRUD 后只重载自己的桶，不清掉别的租户的类型，
/// 也不把自己的定义（Instructions / ToolGroups）暴露给别的租户。
/// </summary>
public class SubAgentRegistryTenantBucketTests
{
    private static Mock<IRepository<SubAgentType, Guid>> RepoWith(params SubAgentType[] rows)
    {
        var repo = new Mock<IRepository<SubAgentType, Guid>>();
        repo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(rows.ToList().BuildMock());
        return repo;
    }

    private static SubAgentType Row(string name, string instructions) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = name,
        Instructions = instructions,
        ToolGroups = ["file"],
        MaxTurns = 10,
        IsEnabled = true
    };

    [Fact]
    public async Task LoadTenantFromStore_TenantA_DoesNotEvictTenantB()
    {
        var registry = new SubAgentRegistry();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();

        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "A's auditor")).Object, tenantA);
        await registry.LoadTenantFromStoreAsync(RepoWith(Row("translator", "B's translator")).Object, tenantB);

        registry.GetForTenant("auditor", tenantA).ShouldNotBeNull();
        registry.GetForTenant("translator", tenantB).ShouldNotBeNull();

        // A 再改一次（整桶重载）只影响 A
        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "A's auditor v2")).Object, tenantA);
        registry.GetForTenant("auditor", tenantA)!.Instructions.ShouldBe("A's auditor v2");
        registry.GetForTenant("translator", tenantB).ShouldNotBeNull();
    }

    [Fact]
    public async Task GetForTenant_ReturnsBuiltInPlusOwnTenantOnly()
    {
        var registry = new SubAgentRegistry();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();

        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "A's secret instructions")).Object, tenantA);
        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "B's own auditor")).Object, tenantB);

        registry.GetForTenant("auditor", tenantA)!.Instructions.ShouldBe("A's secret instructions");
        registry.GetForTenant("auditor", tenantB)!.Instructions.ShouldBe("B's own auditor");
        registry.GetForTenant("auditor", Guid.NewGuid().ToString()).ShouldBeNull();

        var allForB = registry.GetAllForTenant(tenantB);
        allForB.Select(d => d.Name).ShouldContain("general-purpose");
        allForB.Single(d => d.Name == "auditor").Instructions.ShouldBe("B's own auditor");
        allForB.Count.ShouldBe(4);
    }

    [Fact]
    public async Task LegacyOverloads_ReadAndWriteTheDefaultBucket()
    {
        var registry = new SubAgentRegistry();
        var tenantA = Guid.NewGuid().ToString();

        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "A only")).Object, tenantA);
        await registry.LoadFromStoreAsync(RepoWith(Row("shared", "host-level type")).Object);

        registry.Get("auditor").ShouldBeNull();
        registry.Get("shared").ShouldNotBeNull();
        registry.GetAll().Select(d => d.Name).ShouldNotContain("auditor");
        registry.GetForTenant("shared", SubAgentTenantKey.Default).ShouldNotBeNull();
        registry.GetForTenant("shared", tenantA).ShouldBeNull();
    }

    [Fact]
    public async Task LoadTenantFromStore_KeepsCodeRegisteredTypes()
    {
        // 代码经 Register() 注册的类型是全局的，不能被任何租户的重载清掉（旧实现 Clear() 整表会把它们一起清掉）
        var registry = new SubAgentRegistry();
        registry.Register(new SubAgentTypeDefinition(
            Name: "code-registered", Description: "from code", ToolGroups: [], ExcludedToolGroups: [], MaxTurns: 5));

        await registry.LoadTenantFromStoreAsync(RepoWith(Row("auditor", "x")).Object, Guid.NewGuid().ToString());
        await registry.LoadFromStoreAsync(RepoWith().Object);

        registry.Get("code-registered").ShouldNotBeNull();
        registry.GetForTenant("code-registered", Guid.NewGuid().ToString()).ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadAllTenantsFromStore_BucketsRowsByTenantId()
    {
        // 启动期：过滤器关掉后一次读到所有租户的行，按 TenantId 分桶；TenantId 为 null 进默认桶
        var registry = new SubAgentRegistry();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Row("auditor", "A"); rowA.TenantId = tenantA;
        var rowB = Row("auditor", "B"); rowB.TenantId = tenantB;
        var shared = Row("shared", "host");
        var stale = Guid.NewGuid().ToString();
        await registry.LoadTenantFromStoreAsync(RepoWith(Row("gone", "x")).Object, stale);

        await registry.LoadAllTenantsFromStoreAsync(RepoWith(rowA, rowB, shared).Object);

        registry.GetForTenant("auditor", tenantA.ToString())!.Instructions.ShouldBe("A");
        registry.GetForTenant("auditor", tenantB.ToString())!.Instructions.ShouldBe("B");
        registry.Get("auditor").ShouldBeNull();
        registry.Get("shared").ShouldNotBeNull();
        registry.GetForTenant("shared", tenantA.ToString()).ShouldBeNull();
        registry.GetForTenant("gone", stale).ShouldBeNull();
    }

    [Fact]
    public void SubAgentTenantKey_From_MapsNullToDefault()
    {
        SubAgentTenantKey.From(null).ShouldBe(SubAgentTenantKey.Default);
        var id = Guid.NewGuid();
        SubAgentTenantKey.From(id).ShouldBe(id.ToString());
    }
}
