using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillService 单元测试
/// </summary>
public class SkillServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IRepository<SkillEntity, Guid>> _mockRepository;
    private readonly Mock<ISkillRegistry> _mockRegistry;
    private readonly Mock<ISkillTemplateEngine> _mockTemplateEngine;
    private readonly FileSystemSkillStore _fileStore;
    private readonly IServiceProvider _serviceProvider;

    public SkillServiceTests()
    {
        // Initialize Mapster for MapTo<T> / MapToList<T> extension methods
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-skill-svc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockRepository = new Mock<IRepository<SkillEntity, Guid>>();
        _mockRegistry = new Mock<ISkillRegistry>();
        _mockTemplateEngine = new Mock<ISkillTemplateEngine>();

        var aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions
                {
                    Paths = [_tempDir],
                    AllowList = [],
                    DenyList = [],
                    RequireChecksEnabled = false
                }
            }
        });
        var fileStoreLogger = Mock.Of<ILogger<FileSystemSkillStore>>();
        _fileStore = new FileSystemSkillStore(fileStoreLogger, aiOptions);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private SkillService CreateService() => new(
        _serviceProvider,
        _mockRepository.Object,
        _mockRegistry.Object,
        _mockTemplateEngine.Object,
        _fileStore);

    private static SkillDefinition MakeSkill(string slug, string name = "Test Skill", bool enabled = true) => new()
    {
        Slug = slug,
        Name = name,
        Description = "A test skill",
        Content = "Skill content",
        Enabled = enabled,
        Scope = SkillScope.System,
        Source = SkillSource.FileSystem
    };

    // -------------------------------------------------------------------------
    // GetAvailableAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableAsync_ReturnsAllSkillsFromRegistry()
    {
        var skills = new List<SkillDefinition>
        {
            MakeSkill("skill-a", "Skill A"),
            MakeSkill("skill-b", "Skill B")
        };
        _mockRegistry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(skills);

        var service = CreateService();
        var result = await service.GetAvailableAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Count.ShouldBe(2);
        result.Data.Any(d => d.Slug == "skill-a").ShouldBeTrue();
        result.Data.Any(d => d.Slug == "skill-b").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAvailableAsync_EmptyRegistry_ReturnsEmptyList()
    {
        _mockRegistry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService();
        var result = await service.GetAvailableAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetBySlugAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlugAsync_Found_ReturnsDetailDto()
    {
        var skill = MakeSkill("code-review", "Code Review");
        _mockRegistry.Setup(r => r.GetBySlugAsync("code-review", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        var service = CreateService();
        var result = await service.GetBySlugAsync("code-review");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Slug.ShouldBe("code-review");
        result.Data.Name.ShouldBe("Code Review");
    }

    [Fact]
    public async Task GetBySlugAsync_NotFound_Returns404()
    {
        _mockRegistry.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillDefinition?)null);

        var service = CreateService();
        var result = await service.GetBySlugAsync("nonexistent");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_DelegatesToRegistry_ReturnsMatchingSkills()
    {
        var skills = new List<SkillDefinition> { MakeSkill("code-review", "Code Review") };
        _mockRegistry.Setup(r => r.SearchAsync("code", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skills);

        var service = CreateService();
        var result = await service.SearchAsync("code");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Count.ShouldBe(1);
        result.Data[0].Slug.ShouldBe("code-review");

        _mockRegistry.Verify(r => r.SearchAsync("code", 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithCustomMaxResults_PassesMaxResultsToRegistry()
    {
        _mockRegistry.Setup(r => r.SearchAsync("test", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService();
        await service.SearchAsync("test", maxResults: 5);

        _mockRegistry.Verify(r => r.SearchAsync("test", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // ActivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActivateAsync_ValidSlug_ReturnsActivationResult()
    {
        var skill = MakeSkill("code-review", "Code Review");
        _mockRegistry.Setup(r => r.GetBySlugAsync("code-review", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        _mockTemplateEngine
            .Setup(te => te.Render(skill, null))
            .Returns(new SkillRenderResult
            {
                Success = true,
                RenderedContent = "Rendered skill content",
                Errors = [],
                UnusedParameters = []
            });

        var service = CreateService();
        var result = await service.ActivateAsync("code-review");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Slug.ShouldBe("code-review");
        result.Data.Name.ShouldBe("Code Review");
        result.Data.RenderedContent.ShouldBe("Rendered skill content");
        result.Data.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ActivateAsync_SlugNotFound_Returns404()
    {
        _mockRegistry.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillDefinition?)null);

        var service = CreateService();
        var result = await service.ActivateAsync("nonexistent");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task ActivateAsync_DisabledSkill_Returns400()
    {
        var skill = MakeSkill("disabled-skill", enabled: false);
        _mockRegistry.Setup(r => r.GetBySlugAsync("disabled-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        var service = CreateService();
        var result = await service.ActivateAsync("disabled-skill");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ActivateAsync_WithParameters_PassesParametersToTemplateEngine()
    {
        var skill = MakeSkill("parameterized", "Parameterized Skill");
        var parameters = new Dictionary<string, string> { ["lang"] = "csharp" };

        _mockRegistry.Setup(r => r.GetBySlugAsync("parameterized", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        _mockTemplateEngine
            .Setup(te => te.Render(skill, parameters))
            .Returns(new SkillRenderResult
            {
                Success = true,
                RenderedContent = "Rendered with csharp",
                Errors = [],
                UnusedParameters = []
            });

        var service = CreateService();
        var result = await service.ActivateAsync("parameterized", parameters);

        result.Succeeded.ShouldBeTrue();
        result.Data!.RenderedContent.ShouldBe("Rendered with csharp");

        _mockTemplateEngine.Verify(te => te.Render(skill, parameters), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_RenderFailure_Returns400WithError()
    {
        var skill = MakeSkill("broken-skill");
        _mockRegistry.Setup(r => r.GetBySlugAsync("broken-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        _mockTemplateEngine
            .Setup(te => te.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult
            {
                Success = false,
                Errors = ["Missing required parameter: target"],
                UnusedParameters = []
            });

        var service = CreateService();
        var result = await service.ActivateAsync("broken-skill");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var message = result.Message;
        message.ShouldNotBeNull();
        message.ShouldContain("Missing required parameter: target");
    }

    [Fact]
    public async Task ActivateAsync_UnusedParameters_AddedToWarnings()
    {
        var skill = MakeSkill("simple-skill");
        var parameters = new Dictionary<string, string> { ["extra"] = "value" };

        _mockRegistry.Setup(r => r.GetBySlugAsync("simple-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        _mockTemplateEngine
            .Setup(te => te.Render(skill, parameters))
            .Returns(new SkillRenderResult
            {
                Success = true,
                RenderedContent = "Content",
                Errors = [],
                UnusedParameters = ["extra"]
            });

        var service = CreateService();
        var result = await service.ActivateAsync("simple-skill", parameters);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Warnings.ShouldNotBeEmpty();
        result.Data.Warnings.Any(w => w.Contains("extra")).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidInput_InsertsEntityAndInvalidatesCache()
    {
        var input = new CreateSkillDto
        {
            Slug = "my-skill",
            Name = "My Skill",
            Content = "Skill content here",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Slug.ShouldBe("my-skill");
        result.Data.Name.ShouldBe("My Skill");

        _mockRepository.Verify(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRegistry.Verify(r => r.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_InvalidSlug_Returns400()
    {
        var input = new CreateSkillDto
        {
            Slug = "-invalid-slug-",
            Name = "My Skill",
            Content = "Skill content"
        };

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_Returns409()
    {
        var input = new CreateSkillDto
        {
            Slug = "existing-skill",
            Name = "My Skill",
            Content = "Content",
            Scope = SkillScope.Tenant
        };

        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_Found_UpdatesEntityAndInvalidatesCache()
    {
        var id = Guid.NewGuid();
        var entity = new SkillEntity
        {
            Id = id,
            Slug = "my-skill",
            Name = "Old Name",
            Content = "Old content",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        _mockRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockRepository.Setup(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var input = new UpdateSkillDto { Name = "New Name" };

        var service = CreateService();
        var result = await service.UpdateAsync(id, input);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Name.ShouldBe("New Name");

        _mockRepository.Verify(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        _mockRegistry.Verify(r => r.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Returns404()
    {
        _mockRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillEntity?)null);

        var service = CreateService();
        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateSkillDto { Name = "X" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_Found_DeletesEntityAndInvalidatesCache()
    {
        var id = Guid.NewGuid();
        var entity = new SkillEntity
        {
            Id = id,
            Slug = "my-skill",
            Name = "My Skill",
            Content = "Content",
            Scope = SkillScope.Tenant
        };

        _mockRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockRepository.Setup(r => r.DeleteAsync(entity, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.DeleteAsync(id);

        result.Succeeded.ShouldBeTrue();
        _mockRepository.Verify(r => r.DeleteAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        _mockRegistry.Verify(r => r.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        _mockRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillEntity?)null);

        var service = CreateService();
        var result = await service.DeleteAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // ActivateAsync — Requirements Validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActivateAsync_RequirementsNotMet_Returns400()
    {
        var skill = MakeSkill("requires-bins");
        skill.Requirements = new SkillRequirements { Bins = ["nonexistent-binary-abc"] };

        _mockRegistry.Setup(r => r.GetBySlugAsync("requires-bins", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        var mockValidator = new Mock<ISkillRequirementsValidator>();
        mockValidator.Setup(v => v.ValidateRequirements(skill))
            .Returns(new SkillValidationResult { IsValid = false, MissingBins = ["nonexistent-binary-abc"] });

        var service = new SkillService(
            _serviceProvider,
            _mockRepository.Object,
            _mockRegistry.Object,
            _mockTemplateEngine.Object,
            _fileStore,
            mockValidator.Object);

        var result = await service.ActivateAsync("requires-bins");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var message = result.Message;
        message.ShouldNotBeNull();
        message.ShouldContain("requirements not met");
    }

    // -------------------------------------------------------------------------
    // CreateAsync — System Skill Conflict
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ConflictsWithSystemSkill_Returns409()
    {
        // Write a SKILL.md in the temp dir so FileSystemSkillStore finds it
        var skillDir = Path.Combine(_tempDir, "system-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: System Skill\ndescription: A system skill.\n---");

        // Force reload
        _fileStore.InvalidateCache();

        var input = new CreateSkillDto
        {
            Slug = "system-skill",
            Name = "System Skill",
            Content = "Content",
            Scope = SkillScope.Tenant
        };

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    // -------------------------------------------------------------------------
    // CreateAsync — User Scope Without Auth
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_UserScopeWithoutAuth_Returns401()
    {
        var input = new CreateSkillDto
        {
            Slug = "user-skill",
            Name = "User Skill",
            Content = "Content",
            Scope = SkillScope.User
        };

        // No CurrentUser configured in service provider
        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(401);
    }

    // -------------------------------------------------------------------------
    // CreateAsync — With Constraints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithConstraints_StoresConstraintsJson()
    {
        var input = new CreateSkillDto
        {
            Slug = "constrained",
            Name = "Constrained Skill",
            Content = "Content",
            Scope = SkillScope.Tenant,
            AllowedToolGroups = ["code", "search"],
            RequiredModel = "gpt-4",
            RequiredProvider = "openai"
        };

        SkillEntity? capturedEntity = null;
        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SkillEntity, CancellationToken>((e, _) => capturedEntity = e)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        capturedEntity.ShouldNotBeNull();
        capturedEntity!.ConstraintsJson.ShouldNotBeNullOrWhiteSpace();
        capturedEntity.ConstraintsJson.ShouldContain("allowedToolGroups");
        capturedEntity.ConstraintsJson.ShouldContain("requiredModel");
        capturedEntity.ConstraintsJson.ShouldContain("gpt-4");
    }

    // -------------------------------------------------------------------------
    // CreateAsync — no service-layer permission gate (FIX 1 regression proof)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_TenantScope_NoPermissionChecker_Succeeds()
    {
        // Gate is gone: no IPermissionChecker in the service provider → succeeds.
        // (Auth gate is the admin endpoint, not the service layer.)
        var input = new CreateSkillDto
        {
            Slug = "admin-skill",
            Name = "Admin Skill",
            Content = "Content",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_TenantScope_WithDenyAllPermissionChecker_Succeeds()
    {
        // Regression test for PD-5 admin regression: a deny-all IPermissionChecker (simulating
        // Authorization module loaded without the undeclared permission seeded) must NOT block
        // the create — the service-layer gate was removed, so the admin endpoint's own
        // ApiAdminControllerBase authorization is the sole gate.
        var input = new CreateSkillDto
        {
            Slug = "tenant-skill-gate",
            Name = "Tenant Skill",
            Content = "Content",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Build a service provider with a deny-all IPermissionChecker
        var mockPermissionChecker = new Mock<IPermissionChecker>();
        mockPermissionChecker
            .Setup(p => p.IsGrantedAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        mockPermissionChecker
            .Setup(p => p.IsGrantedAnyAsync(It.IsAny<string[]>()))
            .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockPermissionChecker.Object);
        var sp = services.BuildServiceProvider();

        var service = new SkillService(
            sp,
            _mockRepository.Object,
            _mockRegistry.Object,
            _mockTemplateEngine.Object,
            _fileStore);

        var result = await service.CreateAsync(input);

        // Gate is gone: even a deny-all checker must not 403
        result.Succeeded.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // CreateAsync — TenantId persistence (B1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_TenantScope_WithCurrentTenant_PersistsTenantId()
    {
        var tenantId = Guid.NewGuid();

        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId);

        var input = new CreateSkillDto
        {
            Slug = "tenant-skill",
            Name = "Tenant Skill",
            Content = "Content",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        SkillEntity? capturedEntity = null;
        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SkillEntity, CancellationToken>((e, _) => capturedEntity = e)
            .Returns(Task.CompletedTask);

        var service = new SkillService(
            _serviceProvider,
            _mockRepository.Object,
            _mockRegistry.Object,
            _mockTemplateEngine.Object,
            _fileStore,
            requirementsValidator: null,
            currentTenant: mockTenant.Object);

        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        capturedEntity.ShouldNotBeNull();
        capturedEntity!.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public async Task CreateAsync_UserScope_PersistsCurrentTenantId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId);

        var mockUser = new Mock<ICurrentUser>();
        mockUser.Setup(u => u.Id).Returns(userId);

        var input = new CreateSkillDto
        {
            Slug = "user-skill",
            Name = "User Skill",
            Content = "Content",
            Scope = SkillScope.User,
            Enabled = true
        };

        SkillEntity? capturedEntity = null;
        _mockRepository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SkillEntity, CancellationToken>((e, _) => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Build a service provider that provides ICurrentUser
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockUser.Object);
        var sp = services.BuildServiceProvider();

        var service = new SkillService(
            sp,
            _mockRepository.Object,
            _mockRegistry.Object,
            _mockTemplateEngine.Object,
            _fileStore,
            requirementsValidator: null,
            currentTenant: mockTenant.Object);

        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        capturedEntity.ShouldNotBeNull();
        // User-scoped skills now persist the current tenant id (P1-6, 2026-06-20):
        // required so admin tenant-isolation (ApplyTenantVisibility) and DatabaseSkillStore
        // can scope User-scope rows to their owning tenant in multi-tenant deployments.
        capturedEntity!.TenantId.ShouldBe(tenantId);
        capturedEntity.OwnerUserId.ShouldBe(userId);
    }

    [Fact]
    public async Task CreateAsync_TenantScope_DuplicateCheckIncludesTenantId()
    {
        // Two different tenants sharing the same slug:
        // T1 already has "shared-slug" as a Tenant-scoped skill.
        // T2 should be allowed to create the same slug because the dup-check is tenant-scoped.
        // This test proves the predicate includes TenantId by compiling and evaluating it
        // against two in-memory rows — same slug, different tenants.
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        var rowForT1 = new SkillEntity
        {
            Id = Guid.NewGuid(),
            Slug = "shared-slug",
            Scope = SkillScope.Tenant,
            TenantId = tenantId1,
            OwnerUserId = null,
            IsDeleted = false
        };

        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId2); // current tenant is T2

        var input = new CreateSkillDto
        {
            Slug = "shared-slug",
            Name = "Skill",
            Content = "Content",
            Scope = SkillScope.Tenant,
            Enabled = true
        };

        // Capture the predicate passed to AnyAsync and evaluate it against in-memory rows.
        Expression<Func<SkillEntity, bool>>? capturedPredicate = null;
        _mockRepository
            .Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SkillEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<SkillEntity, bool>>, CancellationToken>((pred, _) => capturedPredicate = pred)
            .ReturnsAsync(false); // T2 has no conflict
        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<SkillEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SkillService(
            _serviceProvider,
            _mockRepository.Object,
            _mockRegistry.Object,
            _mockTemplateEngine.Object,
            _fileStore,
            requirementsValidator: null,
            currentTenant: mockTenant.Object);

        var result = await service.CreateAsync(input);

        // Should succeed: no cross-tenant collision
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Slug.ShouldBe("shared-slug");

        // The predicate MUST have been captured (AnyAsync was called)
        capturedPredicate.ShouldNotBeNull("AnyAsync should have been called with a predicate");

        // Compile the predicate and evaluate against in-memory rows to prove tenant-scoping.
        var compiledPredicate = capturedPredicate!.Compile();

        // T1's row: same slug + same scope, but different TenantId → must NOT match (T2 predicate)
        compiledPredicate(rowForT1).ShouldBeFalse(
            "T1's row should not match the T2 duplicate-check predicate");

        // A synthetic T2 row: would be a true duplicate for T2 → must match
        var rowForT2 = new SkillEntity
        {
            Id = Guid.NewGuid(),
            Slug = "shared-slug",
            Scope = SkillScope.Tenant,
            TenantId = tenantId2,
            OwnerUserId = null,
            IsDeleted = false
        };
        compiledPredicate(rowForT2).ShouldBeTrue(
            "T2's own row with the same slug should match the T2 duplicate-check predicate");
    }

    // -------------------------------------------------------------------------
    // UpdateAsync — Ownership Check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UserScope_WrongOwner_Returns403()
    {
        var id = Guid.NewGuid();
        var entity = new SkillEntity
        {
            Id = id,
            Slug = "user-skill",
            Name = "User Skill",
            Content = "Content",
            Scope = SkillScope.User,
            OwnerUserId = Guid.NewGuid() // Different from CurrentUser
        };

        _mockRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var service = CreateService();
        var result = await service.UpdateAsync(id, new UpdateSkillDto { Name = "Hacked" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync — Constraint Merge
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_PartialConstraints_MergesWithExisting()
    {
        var id = Guid.NewGuid();
        var entity = new SkillEntity
        {
            Id = id,
            Slug = "merge-test",
            Name = "Merge Test",
            Content = "Content",
            Scope = SkillScope.Tenant,
            ConstraintsJson = JsonSerializer.Serialize(new { allowedToolGroups = new[] { "code" }, requiredModel = "gpt-3.5" }, TnziJsonDefaults.Options)
        };

        _mockRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockRepository.Setup(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.UpdateAsync(id, new UpdateSkillDto { RequiredModel = "gpt-4" });

        result.Succeeded.ShouldBeTrue();
        // Existing allowedToolGroups should be preserved, model updated
        entity.ConstraintsJson.ShouldContain("code");
        entity.ConstraintsJson.ShouldContain("gpt-4");
        entity.ConstraintsJson.ShouldNotContain("gpt-3.5");
    }

    // -------------------------------------------------------------------------
    // GetUsageStatsAsync — dual-source merged counts
    // -------------------------------------------------------------------------

    private void SetupRepositoryQueryable(List<SkillEntity> data)
    {
        var mock = data.BuildMock();
        _mockRepository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    [Fact]
    public async Task GetUsageStatsAsync_MergedDualSource_CountsFileAndDbSkills()
    {
        // Registry merged view: 2 enabled file-source (System) + 1 enabled DB (Tenant) + 1 disabled DB (User)
        var fileSkillA = MakeSkill("file-a", "File A");
        var fileSkillB = MakeSkill("file-b", "File B");
        var dbSkillTenant = MakeSkill("db-tenant", "DB Tenant");
        dbSkillTenant.Scope = SkillScope.Tenant;
        dbSkillTenant.Source = SkillSource.Database;
        var dbSkillUser = MakeSkill("db-user", "DB User", enabled: false);
        dbSkillUser.Scope = SkillScope.User;
        dbSkillUser.Source = SkillSource.Database;

        _mockRegistry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fileSkillA, fileSkillB, dbSkillTenant, dbSkillUser]);

        // DB rows carry the activation counters
        SetupRepositoryQueryable(
        [
            new SkillEntity { Id = Guid.NewGuid(), Slug = "db-tenant", Scope = SkillScope.Tenant, Enabled = true, ActivationCount = 5 },
            new SkillEntity { Id = Guid.NewGuid(), Slug = "db-user", Scope = SkillScope.User, Enabled = false, ActivationCount = 2 }
        ]);

        var service = CreateService();
        var result = await service.GetUsageStatsAsync();

        result.Succeeded.ShouldBeTrue();
        var stats = result.Data;
        stats.ShouldNotBeNull();
        stats!.TotalSkills.ShouldBe(4);
        stats.EnabledSkills.ShouldBe(3);
        stats.DisabledSkills.ShouldBe(1);
        stats.TenantScopeSkills.ShouldBe(1);
        stats.UserScopeSkills.ShouldBe(1);
        stats.TotalActivations.ShouldBe(7);
    }

    [Fact]
    public async Task GetUsageStatsAsync_FileSourceOnly_EmptyDatabase_CountsFileSkills()
    {
        // Fresh install: no DB rows, but file-source skills exist (the original
        // bug reported all-zero stats while the list showed 20 file skills).
        var fileSkills = Enumerable.Range(1, 20)
            .Select(i => MakeSkill($"file-{i}", $"File Skill {i}"))
            .ToList();

        _mockRegistry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileSkills);
        SetupRepositoryQueryable([]);

        var service = CreateService();
        var result = await service.GetUsageStatsAsync();

        result.Succeeded.ShouldBeTrue();
        var stats = result.Data;
        stats.ShouldNotBeNull();
        stats!.TotalSkills.ShouldBe(20);
        stats.EnabledSkills.ShouldBe(20);
        stats.DisabledSkills.ShouldBe(0);
        stats.TotalActivations.ShouldBe(0);
    }

    [Fact]
    public async Task GetUsageStatsAsync_EmptyRegistryAndDatabase_ReturnsZeros()
    {
        _mockRegistry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupRepositoryQueryable([]);

        var service = CreateService();
        var result = await service.GetUsageStatsAsync();

        result.Succeeded.ShouldBeTrue();
        var stats = result.Data;
        stats.ShouldNotBeNull();
        stats!.TotalSkills.ShouldBe(0);
        stats.EnabledSkills.ShouldBe(0);
        stats.DisabledSkills.ShouldBe(0);
        stats.TenantScopeSkills.ShouldBe(0);
        stats.UserScopeSkills.ShouldBe(0);
        stats.TotalActivations.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync — Ownership Check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_UserScope_WrongOwner_Returns403()
    {
        var id = Guid.NewGuid();
        var entity = new SkillEntity
        {
            Id = id,
            Slug = "user-skill",
            Name = "User Skill",
            Content = "Content",
            Scope = SkillScope.User,
            OwnerUserId = Guid.NewGuid()
        };

        _mockRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var service = CreateService();
        var result = await service.DeleteAsync(id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }
}
