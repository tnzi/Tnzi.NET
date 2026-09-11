using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// 内部技能（<c>IsInternal</c>）在自服务读路径上的可见性。
/// </summary>
/// <remarks>
/// <c>IsInternal</c> 的语义是「不对 Agent 暴露，仅作为其他技能的共享资源依赖」，此前却只在
/// <c>SkillContextProvider</c>（agent 提示词注入）一处被过滤：列表 / 搜索 / 详情三个用户端
/// 读路径照常返回它们，含完整正文。
/// <para>
/// 判据只能是权限码：<c>ISkillService.GetBySlugAsync</c> 与 <c>SearchAsync</c> <b>同时</b>
/// 被自服务控制器与管理端控制器调用，服务无从知道谁在问（与 2026-08-09 那轮
/// Update/Delete 守卫同一形状）。管理端必须仍然看得见内部技能，否则它们变成运维盲区。
/// </para>
/// </remarks>
public class SkillInternalVisibilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IRepository<SkillEntity, Guid>> _repository = new();
    private readonly Mock<ISkillRegistry> _registry = new();
    private readonly Mock<ISkillTemplateEngine> _templateEngine = new();
    private readonly Mock<IPermissionChecker> _permissionChecker = new();
    private readonly FileSystemSkillStore _fileStore;
    private readonly IServiceProvider _serviceProvider;

    public SkillInternalVisibilityTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-skill-internal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions { Paths = [_tempDir], AllowList = [], DenyList = [], RequireChecksEnabled = false }
            }
        });
        _fileStore = new FileSystemSkillStore(Mock.Of<ILogger<FileSystemSkillStore>>(), aiOptions);

        // 默认拒绝一切权限码 = 自服务调用者。
        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _permissionChecker.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    private void GrantSkillView()
        => _permissionChecker.Setup(p => p.IsGrantedAsync("ai.skill.view")).ReturnsAsync(true);

    private SkillService CreateService() => new(
        _serviceProvider,
        _repository.Object,
        _registry.Object,
        _templateEngine.Object,
        _fileStore);

    private static SkillDefinition MakeSkill(string slug, bool isInternal = false) => new()
    {
        Slug = slug,
        Name = slug,
        Description = "A test skill",
        Content = "Full prompt body",
        Enabled = true,
        IsInternal = isInternal,
        Scope = SkillScope.System,
        Source = SkillSource.FileSystem
    };

    [Fact]
    public async Task GetAvailable_SelfService_HidesInternalSkills()
    {
        _registry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSkill("public-one"), MakeSkill("shared-resource", isInternal: true)]);

        var result = await CreateService().GetAvailableAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(d => d.Slug).ShouldBe(["public-one"]);
    }

    [Fact]
    public async Task GetAvailable_WithSkillView_KeepsInternalSkills()
    {
        GrantSkillView();
        _registry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSkill("public-one"), MakeSkill("shared-resource", isInternal: true)]);

        var result = await CreateService().GetAvailableAsync();

        result.Data!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_SelfService_HidesInternalSkills()
    {
        _registry.Setup(r => r.SearchAsync("shared", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSkill("shared-resource", isInternal: true)]);

        var result = await CreateService().SearchAsync("shared");

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBySlug_SelfService_AnswersNotFoundForInternalSkill()
    {
        _registry.Setup(r => r.GetBySlugAsync("shared-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSkill("shared-resource", isInternal: true));

        var result = await CreateService().GetBySlugAsync("shared-resource");

        result.Succeeded.ShouldBeFalse();
        // 404 而不是 403：403 会确认这条 slug 存在。
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetBySlug_WithSkillView_ReturnsInternalSkill()
    {
        GrantSkillView();
        _registry.Setup(r => r.GetBySlugAsync("shared-resource", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSkill("shared-resource", isInternal: true));

        var result = await CreateService().GetBySlugAsync("shared-resource");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Slug.ShouldBe("shared-resource");
    }

    [Fact]
    public async Task GetAvailable_NoInternalSkills_DoesNotConsultPermissions()
    {
        _registry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSkill("public-one"), MakeSkill("public-two")]);

        var result = await CreateService().GetAvailableAsync();

        result.Data!.Count.ShouldBe(2);
        // 常见情况（一条内部技能都没有）不该给每次列表都加一次授权查询。
        _permissionChecker.Verify(p => p.IsGrantedAsync(It.IsAny<string>()), Times.Never);
    }
}
