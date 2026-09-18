
namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillContextProvider 单元测试 - Registry + TemplateEngine + 3 on-demand tools + activation state
/// </summary>
public class SkillContextProviderTests
{
    #region GetContextAsync - Instructions Mode

    [Fact]
    public async Task GetContextAsync_InstructionsMode_InjectsSummaryNotFullContent()
    {
        var skill = new SkillDefinition
        {
            Slug = "code-review",
            Name = "Code Review",
            Description = "Reviews code quality",
            Content = "FULL SKILL CONTENT - should NOT appear in summary",
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
        text.ShouldNotContain("FULL SKILL CONTENT - should NOT appear in summary");
    }

    [Fact]
    public async Task GetContextAsync_NoSkills_ReturnsEmpty()
    {
        var provider = CreateProvider(SkillInjectionMode.Instructions, []);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    #endregion

    #region GetContextAsync - OnDemandTools Mode

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

    #region GetContextAsync - Both Mode

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

    #region Activated skills land in the scoped ISkillActivationTracker

    [Fact]
    public async Task SkillActivate_WritesTheScopedActivationTracker()
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

        var tracker = new SkillActivationTracker();
        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker);

        var injectionBefore = await provider.GetContextAsync([]);
        tracker.ActivatedSkills.ShouldBeEmpty();

        var activateFunc = (AIFunction)injectionBefore.Tools!.First(t => t.Name == "skill_activate");
        await activateFunc.InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["slug"] = "my-skill" }),
            CancellationToken.None);

        // The tracker (shared with the constraint middlewares) now holds the skill,
        // and the next context build exposes it as a callable AIFunction.
        tracker.IsActivated("my-skill").ShouldBeTrue();
        var injectionAfter = await provider.GetContextAsync([]);
        injectionAfter.Tools!.ShouldContain(t => t.Name == "skill_my-skill");
    }

    [Fact]
    public async Task SkillActivate_WithThread_PersistsAndRestoresAcrossProviders()
    {
        var skill = new SkillDefinition { Slug = "ro", Name = "Read Only", Content = "x", Enabled = true, DeniedTools = ["bash"] };
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "x" });

        var threadId = Guid.NewGuid();
        var threadStore = new InMemoryThreadMetadata();

        // Turn 1: fresh scope, activate through the tool.
        var tracker1 = new SkillActivationTracker(threadService: threadStore, registry: registry.Object);
        var provider1 = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker1, threadId);
        var injection1 = await provider1.GetContextAsync([]);
        var activate = (AIFunction)injection1.Tools!.First(t => t.Name == "skill_activate");
        await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), CancellationToken.None);

        // Turn 2: a brand-new scope (new tracker, new provider) must see the activation again.
        var tracker2 = new SkillActivationTracker(threadService: threadStore, registry: registry.Object);
        var provider2 = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker2, threadId);
        tracker2.ActivatedSkills.ShouldBeEmpty("nothing restored until the provider builds context");
        await provider2.GetContextAsync([]);
        tracker2.IsActivated("ro").ShouldBeTrue();

        // Turn 3: deactivate through the tool clears the persisted set.
        var injection2 = await provider2.GetContextAsync([]);
        var deactivate = (AIFunction)injection2.Tools!.First(t => t.Name == "skill_deactivate");
        await deactivate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), CancellationToken.None);

        var tracker3 = new SkillActivationTracker(threadService: threadStore, registry: registry.Object);
        var provider3 = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker3, threadId);
        await provider3.GetContextAsync([]);
        tracker3.ActivatedSkills.ShouldBeEmpty();
    }

    [Fact]
    public async Task SkillActivate_RejectsInternalSkill()
    {
        var skill = new SkillDefinition { Slug = "shared-lib", Name = "Shared", Content = "x", Enabled = true, IsInternal = true };
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("shared-lib", It.IsAny<CancellationToken>())).ReturnsAsync(skill);

        var tracker = new SkillActivationTracker();
        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var result = (await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "shared-lib" }), CancellationToken.None))?.ToString();

        result.ShouldNotBeNull().ShouldContain("Skill not found");
        tracker.ActivatedSkills.ShouldBeEmpty();
        templateEngine.Verify(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
    }

    [Fact]
    public async Task SkillActivate_RespectsAgentsFilterAndRestrictToSlugs()
    {
        var otherAgentsOnly = new SkillDefinition { Slug = "finance-only", Name = "F", Content = "x", Enabled = true, Agents = ["finance-*"] };
        var notGranted = new SkillDefinition { Slug = "not-granted", Name = "N", Content = "x", Enabled = true };
        var granted = new SkillDefinition { Slug = "granted", Name = "G", Content = "x", Enabled = true };
        var (registry, templateEngine) = CreateMocks([]);
        foreach (var s in new[] { otherAgentsOnly, notGranted, granted })
            registry.Setup(r => r.GetBySlugAsync(s.Slug, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        templateEngine.Setup(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "x" });

        var tracker = new SkillActivationTracker();
        var options = new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools };
        var provider = new SkillContextProvider(registry.Object, templateEngine.Object, options, Mock.Of<ILogger<SkillContextProvider>>(),
            agentName: "support-bot", restrictToSlugs: ["granted"], activationTracker: tracker);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        async Task<string?> Activate(string slug) =>
            (await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = slug }), CancellationToken.None))?.ToString();

        (await Activate("finance-only")).ShouldNotBeNull().ShouldContain("Skill not found");
        (await Activate("not-granted")).ShouldNotBeNull().ShouldContain("Skill not found");
        (await Activate("granted")).ShouldNotBeNull().ShouldContain("Skill Activated");
        tracker.ActivatedSkills.Select(s => s.Slug).ShouldBe(["granted"]);
    }

    #endregion

    #region skill_activate tool

    // -------------------------------------------------------------------------
    // 用量统计必须把 agent 侧激活算进去：ActivationCount / LastActivatedAt、/admin/skills/stats、
    // /admin/skills/popular 的唯一写者是 SkillActivatedEventHandler，而事件此前只在 REST 的
    // SkillService.ActivateAsync 发出 —— 技能主要是 agent 在对话里用的，管理端看到的却只是人点按钮的次数。
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SkillActivate_PublishesSkillActivatedEvent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var skill = new SkillDefinition
        {
            Slug = "code-review", Name = "Code Review", Content = "review", Enabled = true,
            Scope = SkillScope.Tenant, Source = SkillSource.Database, TenantId = tenantId
        };
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("code-review", It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "review" });
        var eventBus = new Mock<IEventBus>();
        SkillActivatedEvent? published = null;
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<SkillActivatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SkillActivatedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        var provider = new SkillContextProvider(registry.Object, templateEngine.Object,
            new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools }, Mock.Of<ILogger<SkillContextProvider>>(),
            eventBus: eventBus.Object, userId: userId);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var result = await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "code-review" }), CancellationToken.None);

        (result?.ToString() ?? string.Empty).ShouldContain("Skill Activated");
        published.ShouldNotBeNull();
        published!.Slug.ShouldBe("code-review");
        published.Scope.ShouldBe(SkillScope.Tenant);
        published.Source.ShouldBe(SkillSource.Database);
        published.SkillTenantId.ShouldBe(tenantId);
        published.UserId.ShouldBe(userId);
        eventBus.Verify(b => b.PublishAsync(It.IsAny<SkillActivatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkillActivate_AlreadyActivated_DoesNotPublishAgain()
    {
        var skill = new SkillDefinition { Slug = "code-review", Name = "Code Review", Content = "review", Enabled = true };
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("code-review", It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "review" });
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<SkillActivatedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var provider = new SkillContextProvider(registry.Object, templateEngine.Object,
            new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools }, Mock.Of<ILogger<SkillContextProvider>>(),
            eventBus: eventBus.Object);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "code-review" });

        await activate.InvokeAsync(args, CancellationToken.None);
        await activate.InvokeAsync(args, CancellationToken.None);

        eventBus.Verify(b => b.PublishAsync(It.IsAny<SkillActivatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkillActivate_EventBusThrows_StillActivates()
    {
        // 统计失败不能让工具调用失败：约束照样套上，激活照样返回
        var skill = new SkillDefinition { Slug = "code-review", Name = "Code Review", Content = "review", Enabled = true };
        var (registry, templateEngine) = CreateMocks([]);
        registry.Setup(r => r.GetBySlugAsync("code-review", It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        templateEngine.Setup(e => e.Render(skill, It.IsAny<Dictionary<string, string>?>()))
            .Returns(new SkillRenderResult { Success = true, RenderedContent = "review" });
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<SkillActivatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));
        var tracker = new SkillActivationTracker();

        var provider = new SkillContextProvider(registry.Object, templateEngine.Object,
            new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools }, Mock.Of<ILogger<SkillContextProvider>>(),
            activationTracker: tracker, eventBus: eventBus.Object);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var result = await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "code-review" }), CancellationToken.None);

        (result?.ToString() ?? string.Empty).ShouldContain("Skill Activated");
        tracker.IsActivated("code-review").ShouldBeTrue();
    }

    [Fact]
    public async Task SkillActivate_DisabledSkill_ReturnsNotFound()
    {
        // 文件系统技能的 enabled:false 此前只有 REST 路径尊重；agent 照样能激活。禁用 == 不可见 == 不存在
        var skill = new SkillDefinition { Slug = "retired", Name = "Retired", Content = "x", Enabled = false };
        var (registry, templateEngine) = CreateMocks([skill]);
        registry.Setup(r => r.GetBySlugAsync("retired", It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        var provider = CreateProvider(SkillInjectionMode.Both, registry, templateEngine);
        var injection = await provider.GetContextAsync([]);
        var activate = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");

        var result = await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "retired" }), CancellationToken.None);

        (result?.ToString() ?? string.Empty).ShouldContain("not found");
        templateEngine.Verify(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        string.Join(" ", (injection.Messages ?? []).Select(m => m.Text)).ShouldNotContain("Retired");
    }

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

    /// <summary>
    /// skill_get 必须与 skill_activate 走同一道可见性门：内部技能 / 限定给别的 agent 的技能 /
    /// 不在显式白名单里的技能，正文一律不出、也不能被标成「已加载」。
    /// </summary>
    [Fact]
    public async Task SkillGet_InvisibleSkill_ReturnsNotFoundAndDoesNotMarkLoaded()
    {
        var internalSkill = new SkillDefinition { Slug = "shared-lib", Name = "Shared", Content = "secret body", Enabled = true, IsInternal = true };
        var otherAgentsOnly = new SkillDefinition { Slug = "finance-only", Name = "F", Content = "secret body", Enabled = true, Agents = ["finance-*"] };
        var notGranted = new SkillDefinition { Slug = "not-granted", Name = "N", Content = "secret body", Enabled = true };
        var granted = new SkillDefinition { Slug = "granted", Name = "G", Content = "granted body", Enabled = true };
        var (registry, templateEngine) = CreateMocks([]);
        foreach (var s in new[] { internalSkill, otherAgentsOnly, notGranted, granted })
            registry.Setup(r => r.GetBySlugAsync(s.Slug, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        templateEngine.Setup(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()))
            .Returns((SkillDefinition d, Dictionary<string, string>? _) => new SkillRenderResult { Success = true, RenderedContent = d.Content });

        var loadTracker = new SkillLoadTracker();
        var options = new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools };
        var provider = new SkillContextProvider(registry.Object, templateEngine.Object, options, Mock.Of<ILogger<SkillContextProvider>>(),
            skillLoadTracker: loadTracker, agentName: "support-bot", restrictToSlugs: ["granted"]);
        var injection = await provider.GetContextAsync([]);
        var get = (AIFunction)injection.Tools!.First(t => t.Name == "skill_get");

        async Task<string> Get(string slug) =>
            (await get.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = slug }), CancellationToken.None))?.ToString() ?? string.Empty;

        foreach (var slug in new[] { "shared-lib", "finance-only", "not-granted" })
        {
            var result = await Get(slug);
            result.ShouldContain($"Skill not found: {slug}");
            result.ShouldNotContain("secret body");
            loadTracker.IsLoaded(slug).ShouldBeFalse($"{slug} must not be marked loaded");
        }

        (await Get("granted")).ShouldContain("granted body");
        loadTracker.IsLoaded("granted").ShouldBeTrue();
        templateEngine.Verify(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()), Times.Once);
    }

    [Fact]
    public async Task SkillGet_Batch_SkipsInvisibleSkills()
    {
        var internalSkill = new SkillDefinition { Slug = "shared-lib", Name = "Shared", Content = "secret body", Enabled = true, IsInternal = true };
        var visible = new SkillDefinition { Slug = "visible", Name = "Visible", Content = "visible body", Enabled = true };
        var (registry, templateEngine) = CreateMocks([]);
        foreach (var s in new[] { internalSkill, visible })
            registry.Setup(r => r.GetBySlugAsync(s.Slug, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        templateEngine.Setup(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()))
            .Returns((SkillDefinition d, Dictionary<string, string>? _) => new SkillRenderResult { Success = true, RenderedContent = d.Content });

        var loadTracker = new SkillLoadTracker();
        var options = new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools };
        var provider = new SkillContextProvider(registry.Object, templateEngine.Object, options, Mock.Of<ILogger<SkillContextProvider>>(),
            skillLoadTracker: loadTracker);
        var injection = await provider.GetContextAsync([]);
        var get = (AIFunction)injection.Tools!.First(t => t.Name == "skill_get");

        var result = (await get.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "shared-lib, visible" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("Skill not found: shared-lib");
        result.ShouldNotContain("secret body");
        result.ShouldContain("visible body");
        loadTracker.IsLoaded("shared-lib").ShouldBeFalse();
        loadTracker.IsLoaded("visible").ShouldBeTrue();
    }

    [Fact]
    public async Task SkillGetResource_InvisibleSkill_ReturnsNotFound()
    {
        var internalSkill = new SkillDefinition
        {
            Slug = "shared-lib", Name = "Shared", Content = "x", Enabled = true, IsInternal = true,
            Resources = new Dictionary<string, string> { ["scripts/run.py"] = "print('secret')" }
        };
        var otherAgentsOnly = new SkillDefinition
        {
            Slug = "finance-only", Name = "F", Content = "x", Enabled = true, Agents = ["finance-*"],
            Resources = new Dictionary<string, string> { ["scripts/run.py"] = "print('secret')" }
        };
        var visible = new SkillDefinition
        {
            Slug = "visible", Name = "V", Content = "x", Enabled = true,
            Resources = new Dictionary<string, string> { ["scripts/run.py"] = "print('ok')" }
        };
        var (registry, templateEngine) = CreateMocks([]);
        foreach (var s in new[] { internalSkill, otherAgentsOnly, visible })
            registry.Setup(r => r.GetBySlugAsync(s.Slug, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var options = new SkillsOptions { InjectionMode = SkillInjectionMode.OnDemandTools };
        var provider = new SkillContextProvider(registry.Object, templateEngine.Object, options, Mock.Of<ILogger<SkillContextProvider>>(),
            agentName: "support-bot");
        var injection = await provider.GetContextAsync([]);
        var getResource = (AIFunction)injection.Tools!.First(t => t.Name == "skill_get_resource");

        async Task<string> GetResource(string slug) =>
            (await getResource.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = slug, ["path"] = "scripts/run.py" }),
                CancellationToken.None))?.ToString() ?? string.Empty;

        foreach (var slug in new[] { "shared-lib", "finance-only" })
        {
            var result = await GetResource(slug);
            result.ShouldContain($"Skill not found: {slug}");
            result.ShouldNotContain("secret");
        }

        (await GetResource("visible")).ShouldBe("print('ok')");
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

        var tracker = new SkillActivationTracker();
        var provider = CreateProvider(SkillInjectionMode.OnDemandTools, registry, templateEngine, tracker);
        var injection = await provider.GetContextAsync([]);

        // Activate first
        var activateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_activate");
        await activateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "active-skill" }),
            CancellationToken.None);

        // Verify activated
        tracker.ActivatedSkills.Count.ShouldBe(1);

        // Deactivate
        var deactivateFunc = (AIFunction)injection.Tools!.First(t => t.Name == "skill_deactivate");
        var result = (await deactivateFunc.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "active-skill" }),
            CancellationToken.None))?.ToString() ?? string.Empty;

        result.ShouldContain("deactivated");

        // Verify deactivated
        tracker.ActivatedSkills.ShouldBeEmpty();
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
        Mock<ISkillTemplateEngine> templateEngine,
        ISkillActivationTracker? tracker = null,
        Guid? threadId = null)
    {
        var options = new SkillsOptions { InjectionMode = mode };
        var logger = Mock.Of<ILogger<SkillContextProvider>>();
        return new SkillContextProvider(registry.Object, templateEngine.Object, options, logger,
            activationTracker: tracker, threadId: threadId);
    }

    /// <summary>Thread metadata store that only implements the two key/value members; everything else is unused here.</summary>
    internal sealed class InMemoryThreadMetadata : IAgentThreadInternalService
    {
        private readonly Dictionary<(Guid, string), string> _values = [];

        public IReadOnlyDictionary<(Guid, string), string> Values => _values;

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult(_values.TryGetValue((threadId, key), out var v) ? v : null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
        {
            if (valueJson == null) _values.Remove((threadId, key));
            else _values[(threadId, key)] = valueJson;
            return Task.CompletedTask;
        }

        public Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
            => throw new NotSupportedException();
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
