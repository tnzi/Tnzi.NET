using Moq;
using Tnzi.AI.Engine;
using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Options;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillContextProvider 单元测试 — Registry + TemplateEngine + 3 on-demand tools + activation state
/// </summary>
public class SkillContextProviderTests
{
    #region GetContextAsync — Instructions Mode

    [Fact]
    public async Task GetContextAsync_InstructionsMode_InjectsSummaryNotFullContent()
    {
        var skill = new SkillDefinition
        {
            Slug = "code-review",
            Name = "Code Review",
            Description = "Reviews code quality",
            Content = "FULL SKILL CONTENT — should NOT appear in summary",
            Enabled = true
        };
        var provider = CreateProvider(SkillInjectionMode.Instructions, [skill]);

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Count.ShouldBe(1);
        var text = injection.Messages[0].Text!;
        text.ShouldContain("Code Review");
        text.ShouldContain("Reviews code quality");
        // Summary mode must NOT inject full content
        text.ShouldNotContain("FULL SKILL CONTENT — should NOT appear in summary");
    }

    [Fact]
    public async Task GetContextAsync_NoSkills_ReturnsEmpty()
    {
        var provider = CreateProvider(SkillInjectionMode.Instructions, []);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    #endregion

    #region GetContextAsync — OnDemandTools Mode

    [Fact]
    public async Task GetContextAsync_OnDemandMode_InjectsThreeTools()
    {
        var (registry, templateEngine) = CreateMocks([]);
        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        injection.Tools.ShouldNotBeNull();
        injection.Tools!.Count.ShouldBe(5); // skill_search + skill_get + skill_activate + skill_deactivate
        injection.Tools.Select(t => t.Name).ShouldContain("skill_search");
        injection.Tools.Select(t => t.Name).ShouldContain("skill_get");
        injection.Tools.Select(t => t.Name).ShouldContain("skill_activate");
        injection.Tools.Select(t => t.Name).ShouldContain("skill_deactivate");
    }

    #endregion

    #region GetContextAsync — Both Mode

    [Fact]
    public async Task GetContextAsync_BothMode_InjectsSummaryAndTools()
    {
        var skill = new SkillDefinition
        {
            Slug = "test-skill",
            Name = "Test Skill",
            Description = "Desc",
            Enabled = true
        };
        var provider = CreateProvider(SkillInjectionMode.Both, [skill]);

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Count.ShouldBeGreaterThan(0);
        injection.Tools.ShouldNotBeNull();
        injection.Tools!.Count.ShouldBe(5);
    }

    #endregion

    #region Internal Skills Filtering

    [Fact]
    public async Task GetContextAsync_ExcludesInternalSkills()
    {
        var internalSkill = new SkillDefinition
        {
            Slug = "office-scripts",
            Name = "Office Scripts",
            Description = "Shared dependency",
            IsInternal = true,
            Enabled = true
        };
        var normalSkill = new SkillDefinition
        {
            Slug = "code-review",
            Name = "Code Review",
            Description = "Reviews code",
            Enabled = true
        };
        var provider = CreateProvider(SkillInjectionMode.Instructions, [internalSkill, normalSkill]);

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        var text = injection.Messages![0].Text!;
        text.ShouldContain("Code Review");
        text.ShouldNotContain("Office Scripts"); // Internal skill should be excluded
    }

    #endregion

    #region Activated Skills in ContextInjection.ActiveSkills

    [Fact]
    public async Task GetContextAsync_IncludesActivatedSkillsInActiveSkills()
    {
        var skill = new SkillDefinition
        {
            Slug = "my-skill",
            Name = "My Skill",
            Content = "Do the thing.",
            Enabled = true
        };

        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("my-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Do the thing." });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);

        // Before activation: no ActiveSkills
        var injectionBefore = await provider.GetContextAsync([]);
        injectionBefore.ActiveSkills.ShouldBeNull();

        // Get the activate tool and invoke it
        var activateFunc = (AIFunction)injectionBefore.Tools!.First(t => t.Name == "skill_activate");
        await activateFunc.InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["slug"] = "my-skill" }),
            CancellationToken.None);

        // After activation: ActiveSkills should contain the skill
        var injectionAfter = await provider.GetContextAsync([]);
        injectionAfter.ActiveSkills.ShouldNotBeNull();
        injectionAfter.ActiveSkills!.Count.ShouldBe(1);
        injectionAfter.ActiveSkills[0].Slug.ShouldBe("my-skill");
    }

    #endregion

    #region skill_activate tool

    [Fact]
    public async Task SkillActivate_RendersTemplate_WithParameters()
    {
        var skill = new SkillDefinition
        {
            Slug = "parameterized-skill",
            Name = "Parameterized Skill",
            Content = "Hello {{name}}",
            Parameters = [new SkillParameter { Name = "name", Required = true }],
            Enabled = true
        };

        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("parameterized-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine
            .Setup(e => e.Render(skill, It.Is<Dictionary<string, string>?>(d => d != null && d["name"] == "World")))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Hello World" });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);
        var activateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var result = await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "parameterized-skill", ["parameters"] = "{\"name\":\"World\"}" }),
            CancellationToken.None);

        var resultStr = result?.ToString() ?? string.Empty;
        resultStr.ShouldContain("Parameterized Skill");
        resultStr.ShouldContain("Hello World");
        templateEngine.Verify(
            e => e.Render(skill, It.Is<Dictionary<string, string>?>(d => d != null && d["name"] == "World")),
            Times.Once);
    }

    [Fact]
    public async Task SkillActivate_Idempotent_SameSkillTwice()
    {
        var skill = new SkillDefinition
        {
            Slug = "idempotent-skill",
            Name = "Idempotent Skill",
            Content = "Content",
            Enabled = true
        };

        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("idempotent-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, null))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Content" });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);
        var activateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var firstResult = (await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "idempotent-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        var secondResult = (await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "idempotent-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        firstResult.ShouldContain("Skill Activated");
        secondResult.ShouldContain("already activated");
    }

    #endregion

    #region skill_search tool

    [Fact]
    public async Task SkillSearch_DelegatesToRegistry()
    {
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.SearchAsync("keyword", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SkillDefinition>)[new SkillDefinition { Slug = "found", Name = "Found Skill", Enabled = true }]);

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);
        var searchFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_search");

        var result = (await searchFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["keyword"] = "keyword" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("Found Skill");
        registry.Verify(r => r.SearchAsync("keyword", 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region skill_get tool

    [Fact]
    public async Task SkillGet_RendersWithDefaults()
    {
        var skill = new SkillDefinition
        {
            Slug = "get-skill",
            Name = "Get Skill",
            Content = "Raw content",
            Enabled = true
        };

        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("get-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, null))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Rendered content" });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);
        var getFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_get");

        var result = (await getFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "get-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("Rendered content");
        templateEngine.Verify(e => e.Render(skill, null), Times.Once);
    }

    #endregion

    #region OnCompletedAsync

    [Fact]
    public async Task OnCompletedAsync_ReturnsCompletedTask()
    {
        var provider = CreateProvider(SkillInjectionMode.Instructions, []);

        // Should not throw
        await provider.OnCompletedAsync([]);
    }

    #endregion

    #region skill_deactivate

    [Fact]
    public async Task SkillDeactivate_RemovesActivatedSkill()
    {
        var skill = new SkillDefinition
        {
            Slug = "active-skill",
            Name = "Active Skill",
            Content = "content",
            Enabled = true
        };
        var (registry, templateEngine) = CreateMocks([skill]);
        registry.Setup(r => r.GetBySlugAsync("active-skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, null))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Rendered" });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);

        // Activate first
        var activateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");
        await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "active-skill" }),
            CancellationToken.None);

        // Verify activated
        var injectionAfterActivate = await provider.GetContextAsync([]);
        injectionAfterActivate.ActiveSkills.ShouldNotBeNull();
        injectionAfterActivate.ActiveSkills!.Count.ShouldBe(1);

        // Deactivate
        var deactivateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_deactivate");
        var result = (await deactivateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "active-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("deactivated");

        // Verify deactivated
        var injectionAfterDeactivate = await provider.GetContextAsync([]);
        (injectionAfterDeactivate.ActiveSkills == null || injectionAfterDeactivate.ActiveSkills.Count == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task SkillDeactivate_NotActive_ReturnsNotActiveMessage()
    {
        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, []);
        var injection = await provider.GetContextAsync([]);

        var deactivateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_deactivate");
        var result = (await deactivateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "nonexistent" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("was not active");
    }

    [Fact]
    public async Task SkillDeactivate_CaseInsensitive()
    {
        var skill = new SkillDefinition
        {
            Slug = "My-Skill",
            Name = "My Skill",
            Content = "content",
            Enabled = true
        };
        var (registry, templateEngine) = CreateMocks([skill]);
        registry.Setup(r => r.GetBySlugAsync("My-Skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, null))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "Rendered" });

        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);

        // Activate with original case
        var activateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");
        await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "My-Skill" }),
            CancellationToken.None);

        // Deactivate with different case
        var deactivateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_deactivate");
        var result = (await deactivateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "my-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("deactivated");
    }

    #endregion

    #region Helpers

    private static SkillContextProvider CreateProvider(SkillInjectionMode mode, List<SkillDefinition> skills)
    {
        var (registry, templateEngine) = CreateMocks(skills);
        return CreateProvider(mode, registry, templateEngine);
    }

    private static SkillContextProvider CreateProvider(
        SkillInjectionMode mode,
        Mock<ISkillRegistry> registry,
        Mock<ISkillTemplateEngine> templateEngine)
    {
        var options = new SkillsOptions { InjectionMode = mode };
        var logger = Mock.Of<ILogger<SkillContextProvider>>();
        return new SkillContextProvider(registry.Object, templateEngine.Object, options, logger);
    }

    private static (Mock<ISkillRegistry> registry, Mock<ISkillTemplateEngine> templateEngine) CreateMocks(
        List<SkillDefinition> skills)
    {
        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SkillDefinition>)skills);

        var templateEngine = new Mock<ISkillTemplateEngine>();
        return (registry, templateEngine);
    }

    #endregion
}
