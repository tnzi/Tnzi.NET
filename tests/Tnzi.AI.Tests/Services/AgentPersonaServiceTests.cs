namespace Tnzi.AI.Tests.Services;

/// <summary>
/// AgentPersonaService 单元测试 — 覆盖 CRUD、ResourceScope 多租户可见性、写入路径隔离。
/// 使用 MockQueryable 模拟 IRepository.AsQueryable()，与 ProviderServiceTests 风格一致。
/// </summary>
public class AgentPersonaServiceTests
{
    private readonly Mock<IRepository<AgentPersona, Guid>> _repository = new();
    private readonly IServiceProvider _serviceProvider;

    public AgentPersonaServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentPersonaService CreateService(ICurrentTenant? currentTenant = null) =>
        new(_serviceProvider, _repository.Object, currentTenant);

    private void SetupQueryable(List<AgentPersona> data)
    {
        var mock = data.BuildMock();
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
        _repository.Setup(r => r.AsQueryable()).Returns(mock);
    }

    private static AgentPersona MakePersona(
        string name = "Test Persona",
        string slug = "test-persona",
        ResourceScope scope = ResourceScope.System,
        Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        Content = $"You are {name}.",
        Description = $"{name} description",
        Scope = scope,
        TenantId = tenantId
    };

    // -------------------------------------------------------------------------
    // Regression: System personas NOT hidden under MT (the bug being fixed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_TenantB_CanSeeSystemPersona_CannotSeeTenantA()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        SetupQueryable(new List<AgentPersona>
        {
            MakePersona("System Persona", "system-persona", ResourceScope.System),
            MakePersona("Tenant A Persona", "tenant-a-persona", ResourceScope.Tenant, tenantId: tenantA),
            MakePersona("Tenant B Persona", "tenant-b-persona", ResourceScope.Tenant, tenantId: tenantB),
        });

        var svc = CreateService(new StubCurrentTenant(tenantB));

        var result = await svc.GetListAsync(new AgentPersonaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Select(p => p.Slug).ShouldContain("system-persona");
        result.Data.Items.Select(p => p.Slug).ShouldContain("tenant-b-persona");
        result.Data.Items.Select(p => p.Slug).ShouldNotContain("tenant-a-persona");
    }

    [Fact]
    public async Task GetListAsync_TenantA_CanSeeBothSystemAndOwnTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        SetupQueryable(new List<AgentPersona>
        {
            MakePersona("System Persona", "system-persona", ResourceScope.System),
            MakePersona("Tenant A Persona", "tenant-a-persona", ResourceScope.Tenant, tenantId: tenantA),
            MakePersona("Tenant B Persona", "tenant-b-persona", ResourceScope.Tenant, tenantId: tenantB),
        });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.GetListAsync(new AgentPersonaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Select(p => p.Slug).ShouldContain("system-persona");
        result.Data.Items.Select(p => p.Slug).ShouldContain("tenant-a-persona");
        result.Data.Items.Select(p => p.Slug).ShouldNotContain("tenant-b-persona");
    }

    [Fact]
    public async Task GetListAsync_NoTenant_ReturnsAll()
    {
        var tenantA = Guid.NewGuid();
        SetupQueryable(new List<AgentPersona>
        {
            MakePersona("System Persona", "system-persona", ResourceScope.System),
            MakePersona("Tenant A Persona", "tenant-a-persona", ResourceScope.Tenant, tenantId: tenantA),
        });

        // 单租户模式（无 ICurrentTenant）— 返回全部
        var svc = CreateService(currentTenant: null);

        var result = await svc.GetListAsync(new AgentPersonaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // Write-path isolation: cross-tenant 404, system 403
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_TenantA_OnTenantBPersona_Returns404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantBPersona = MakePersona("B Persona", "b-persona", ResourceScope.Tenant, tenantId: tenantB);
        SetupQueryable(new List<AgentPersona> { tenantBPersona });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.UpdateAsync(tenantBPersona.Id, new UpdateAgentPersonaDto { Name = "hacked" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_TenantA_OnTenantBPersona_Returns404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantBPersona = MakePersona("B Persona", "b-persona", ResourceScope.Tenant, tenantId: tenantB);
        SetupQueryable(new List<AgentPersona> { tenantBPersona });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.DeleteAsync(tenantBPersona.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateAsync_TenantA_OnSystemPersona_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var systemPersona = MakePersona("System", "system", ResourceScope.System);
        SetupQueryable(new List<AgentPersona> { systemPersona });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.UpdateAsync(systemPersona.Id, new UpdateAgentPersonaDto { Name = "tampered" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task DeleteAsync_TenantA_OnSystemPersona_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var systemPersona = MakePersona("System", "system", ResourceScope.System);
        SetupQueryable(new List<AgentPersona> { systemPersona });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.DeleteAsync(systemPersona.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task UpdateAsync_SingleTenantMode_CanUpdateSystemPersona()
    {
        var systemPersona = MakePersona("System", "system", ResourceScope.System);
        SetupQueryable(new List<AgentPersona> { systemPersona });
        _repository.Setup(r => r.UpdateAsync(It.IsAny<AgentPersona>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 无 ICurrentTenant — 单租户模式，无保护
        var svc = CreateService(currentTenant: null);

        var result = await svc.UpdateAsync(systemPersona.Id, new UpdateAgentPersonaDto { Name = "admin update" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_SingleTenantMode_CanDeleteSystemPersona()
    {
        var systemPersona = MakePersona("System", "system", ResourceScope.System);
        SetupQueryable(new List<AgentPersona> { systemPersona });

        // 无 ICurrentTenant — 单租户模式，无保护
        var svc = CreateService(currentTenant: null);

        var result = await svc.DeleteAsync(systemPersona.Id);

        result.Succeeded.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Scope filter in GetListAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_FilterByScope_ReturnsMatchingOnly()
    {
        SetupQueryable(new List<AgentPersona>
        {
            MakePersona("System", "system", ResourceScope.System),
            MakePersona("Tenant", "tenant-one", ResourceScope.Tenant, tenantId: Guid.NewGuid()),
        });

        // 单租户模式可以按 Scope 过滤
        var svc = CreateService(currentTenant: null);

        var result = await svc.GetListAsync(new AgentPersonaQueryDto
        {
            Scope = ResourceScope.System,
            PageIndex = 1,
            PageSize = 10
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].Slug.ShouldBe("system");
    }

    // -------------------------------------------------------------------------
    // Create: scope stamping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithTenant_StampsTenantScope()
    {
        SetupQueryable(new List<AgentPersona>());
        AgentPersona? inserted = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<AgentPersona>(), It.IsAny<CancellationToken>()))
            .Callback<AgentPersona, CancellationToken>((p, _) => inserted = p)
            .Returns(Task.CompletedTask);

        var tenantA = Guid.NewGuid();
        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.CreateAsync(new CreateAgentPersonaDto
        {
            Name = "My Persona",
            Slug = "my-persona",
            Content = "My content."
        });

        result.Succeeded.ShouldBeTrue();
        inserted.ShouldNotBeNull();
        inserted!.Scope.ShouldBe(ResourceScope.Tenant);
        inserted.TenantId.ShouldBe(tenantA);
    }

    [Fact]
    public async Task CreateAsync_NoTenant_DefaultsToSystemScope()
    {
        SetupQueryable(new List<AgentPersona>());
        AgentPersona? inserted = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<AgentPersona>(), It.IsAny<CancellationToken>()))
            .Callback<AgentPersona, CancellationToken>((p, _) => inserted = p)
            .Returns(Task.CompletedTask);

        var svc = CreateService(currentTenant: null);

        var result = await svc.CreateAsync(new CreateAgentPersonaDto
        {
            Name = "Global Persona",
            Slug = "global-persona",
            Content = "Global content."
        });

        result.Succeeded.ShouldBeTrue();
        inserted.ShouldNotBeNull();
        inserted!.Scope.ShouldBe(ResourceScope.System);
        inserted.TenantId.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Existing basic CRUD tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlugAsync_Exists_ReturnsOk()
    {
        var persona = MakePersona("Test", "test");
        SetupQueryable(new List<AgentPersona> { persona });

        var svc = CreateService();
        var result = await svc.GetBySlugAsync("test");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Slug.ShouldBe("test");
    }

    [Fact]
    public async Task GetBySlugAsync_NotFound_ReturnsFail()
    {
        SetupQueryable(new List<AgentPersona>());
        _repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentPersona, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPersona?)null);

        var svc = CreateService();
        var result = await svc.GetBySlugAsync("nonexistent");

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        SetupQueryable(new List<AgentPersona>());

        var svc = CreateService();
        var result = await svc.DeleteAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_TenantOwnPersona_Succeeds()
    {
        var tenantA = Guid.NewGuid();
        var persona = MakePersona("Mine", "mine", ResourceScope.Tenant, tenantId: tenantA);
        SetupQueryable(new List<AgentPersona> { persona });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.DeleteAsync(persona.Id);

        result.Succeeded.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // DTO contains Scope field
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_DtoContainsScopeField()
    {
        SetupQueryable(new List<AgentPersona>
        {
            MakePersona("System", "system", ResourceScope.System),
        });

        var svc = CreateService(currentTenant: null);
        var result = await svc.GetListAsync(new AgentPersonaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Items[0].Scope.ShouldBe(ResourceScope.System);
    }

    // -------------------------------------------------------------------------
    // Stubs
    // -------------------------------------------------------------------------

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => true;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOpDisposable();
        private sealed class NoOpDisposable : IDisposable { public void Dispose() { } }
    }
}
