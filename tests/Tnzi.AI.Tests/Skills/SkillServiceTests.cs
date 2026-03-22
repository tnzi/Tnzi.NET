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
        result.Message.ShouldContain("Missing required parameter: target");
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
}
