using Mapster;
using MapsterMapper;
using Tnzi.Mapster;

namespace Tnzi.AI.Tests;

/// <summary>
/// AgentVersionRouter 单元测试
/// </summary>
public class AgentVersionRouterTests
{
    public AgentVersionRouterTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);
    }

    #region RouteAsync Tests

    [Fact]
    public async Task RouteAsync_NoConfiguration_ReturnsPassthrough()
    {
        var agent = CreateAgent(configuration: null);
        var router = CreateRouter();

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
        result.SelectedVersion.ShouldBeNull();
        result.SelectedVariant.ShouldBeNull();
    }

    [Fact]
    public async Task RouteAsync_EmptyConfiguration_ReturnsPassthrough()
    {
        var agent = CreateAgent(configuration: "{}");
        var router = CreateRouter();

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task RouteAsync_AbTestDisabled_ReturnsPassthrough()
    {
        var config = """{"abTest":{"enabled":false,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
        var agent = CreateAgent(configuration: config);
        var router = CreateRouter();

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task RouteAsync_IdenticalVersions_ReturnsPassthrough()
    {
        var config = """{"abTest":{"enabled":true,"versionA":3,"versionB":3,"trafficPercentB":50}}""";
        var agent = CreateAgent(configuration: config);
        var router = CreateRouter();

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task RouteAsync_VersionNotFound_ReturnsPassthrough()
    {
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
        var agent = CreateAgent(configuration: config);
        var router = CreateRouter(versions: []);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task RouteAsync_TrafficPercentB100_AlwaysSelectsVariantB()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);

        var versionB = CreateVersionEntity(agentId, 2, "Version B Agent");
        var router = CreateRouter(versions: [versionB]);

        for (var i = 0; i < 10; i++)
        {
            var result = await router.RouteAsync(agent, CancellationToken.None);

            result.IsAbTestRouted.ShouldBeTrue();
            result.SelectedVariant.ShouldBe("B");
            ((int)result.SelectedVersion!).ShouldBe(2);
            result.Agent.Name.ShouldBe("Version B Agent");
            result.Agent.Id.ShouldBe(agentId);
        }
    }

    [Fact]
    public async Task RouteAsync_TrafficPercentB0_AlwaysSelectsVariantA()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":0}}""";
        var agent = CreateAgent(agentId, config);

        var versionA = CreateVersionEntity(agentId, 1, "Version A Agent");
        var router = CreateRouter(versions: [versionA]);

        for (var i = 0; i < 10; i++)
        {
            var result = await router.RouteAsync(agent, CancellationToken.None);

            result.IsAbTestRouted.ShouldBeTrue();
            result.SelectedVariant.ShouldBe("A");
            ((int)result.SelectedVersion!).ShouldBe(1);
            result.Agent.Name.ShouldBe("Version A Agent");
        }
    }

    [Fact]
    public async Task RouteAsync_TrafficSplit_IsDeterministicForSameAgent()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
        var agent = CreateAgent(agentId, config);

        var versionA = CreateVersionEntity(agentId, 1, "Version A Agent");
        var versionB = CreateVersionEntity(agentId, 2, "Version B Agent");
        var router = CreateRouter(versions: [versionA, versionB]);

        // 同一 Agent 每次路由应得到相同结果（确定性分配）
        var firstResult = await router.RouteAsync(agent, CancellationToken.None);
        firstResult.IsAbTestRouted.ShouldBeTrue();

        for (var i = 0; i < 10; i++)
        {
            var result = await router.RouteAsync(agent, CancellationToken.None);
            result.SelectedVariant.ShouldBe(firstResult.SelectedVariant);
            result.SelectedVersion.ShouldBe(firstResult.SelectedVersion);
        }
    }

    [Fact]
    public async Task RouteAsync_TrafficSplit_DifferentAgentsMayGetDifferentVariants()
    {
        var versionA1 = new List<AgentVersion>();
        var versionB1 = new List<AgentVersion>();
        var variantSet = new HashSet<string>();

        // 使用不同 AgentId 创建多个 Agent，验证确定性哈希可以产生不同结果
        for (var i = 0; i < 20; i++)
        {
            var agentId = Guid.NewGuid();
            var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
            var agent = CreateAgent(agentId, config);

            var va = CreateVersionEntity(agentId, 1, "Version A Agent");
            var vb = CreateVersionEntity(agentId, 2, "Version B Agent");
            var router = CreateRouter(versions: [va, vb]);

            var result = await router.RouteAsync(agent, CancellationToken.None);
            result.IsAbTestRouted.ShouldBeTrue();
            variantSet.Add(result.SelectedVariant!);
        }

        // 20 个不同 Agent 在 50% 分配下，应至少出现两种 variant
        variantSet.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RouteAsync_DoesNotMutateOriginalAgent()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);
        var originalName = agent.Name;

        var versionB = CreateVersionEntity(agentId, 2, "Different Name");
        var router = CreateRouter(versions: [versionB]);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        agent.Name.ShouldBe(originalName);
        result.Agent.ShouldNotBeSameAs(agent);
        result.Agent.Name.ShouldBe("Different Name");
    }

    [Fact]
    public async Task RouteAsync_PreservesOriginalAgentId()
    {
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);
        agent.TenantId = tenantId;

        var versionB = CreateVersionEntity(agentId, 2, "B Agent");
        var router = CreateRouter(versions: [versionB]);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.Agent.Id.ShouldBe(agentId);
        result.Agent.TenantId.ShouldBe(tenantId);
    }

    /// <summary>
    /// A/B-routed variants must be able to diverge persona. Historical bug:
    /// ApplySnapshot hardcoded `PersonaId = original.PersonaId`, so any A/B test
    /// targeting different Soul content was silently no-op — both A and B routed
    /// through the live PersonaId, defeating the whole purpose of the experiment.
    /// Snapshot.PersonaId must win when present.
    /// </summary>
    [Fact]
    public async Task RouteAsync_SnapshotPersonaIdWinsOverOriginal()
    {
        var agentId = Guid.NewGuid();
        var livePersonaId = Guid.NewGuid();
        var variantBPersonaId = Guid.NewGuid();

        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);
        agent.PersonaId = livePersonaId; // current "control" persona

        // Version B snapshot ships a DIFFERENT persona
        var versionB = CreateVersionEntityWithPersona(agentId, 2, "Variant B", variantBPersonaId);
        var router = CreateRouter(versions: [versionB]);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeTrue();
        result.SelectedVariant.ShouldBe("B");
        // CRITICAL: routed Agent.PersonaId must be the snapshot's, not original's.
        // If this regresses, A/B experiments on persona become silently meaningless.
        result.Agent.PersonaId.ShouldBe(variantBPersonaId);
        // Original control Agent unchanged
        agent.PersonaId.ShouldBe(livePersonaId);
    }

    /// <summary>
    /// Legacy snapshot rows (taken before PersonaId was added to AgentConfigSnapshot)
    /// deserialize with PersonaId = null. ApplySnapshot must fall back to original.PersonaId
    /// so an A/B test referencing a pre-existing version doesn't drop persona entirely.
    /// </summary>
    [Fact]
    public async Task RouteAsync_LegacySnapshotWithoutPersonaId_FallsBackToOriginal()
    {
        var agentId = Guid.NewGuid();
        var livePersonaId = Guid.NewGuid();

        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);
        agent.PersonaId = livePersonaId;

        // Legacy snapshot without PersonaId (mirrors AgentVersion rows from before this PR)
        var legacyVersion = CreateVersionEntity(agentId, 2, "Legacy B");
        var router = CreateRouter(versions: [legacyVersion]);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeTrue();
        // Fallback path keeps the live persona — no surprise loss
        result.Agent.PersonaId.ShouldBe(livePersonaId);
    }

    [Fact]
    public async Task RouteAsync_InvalidSnapshotJson_ReturnsPassthrough()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":100}}""";
        var agent = CreateAgent(agentId, config);

        var badVersion = new AgentVersion
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Version = 2,
            ConfigSnapshot = "not-valid-json"
        };
        var router = CreateRouter(versions: [badVersion]);

        var result = await router.RouteAsync(agent, CancellationToken.None);

        result.IsAbTestRouted.ShouldBeFalse();
        result.Agent.ShouldBeSameAs(agent);
    }

    #endregion

    #region ExtractAbTestConfig Tests

    [Fact]
    public void ExtractAbTestConfig_ValidJson_ReturnsConfig()
    {
        var json = """{"abTest":{"enabled":true,"versionA":3,"versionB":5,"trafficPercentB":20}}""";

        var config = AgentVersionRouter.ExtractAbTestConfig(json);

        config.ShouldNotBeNull();
        config.Enabled.ShouldBeTrue();
        config.VersionA.ShouldBe(3);
        config.VersionB.ShouldBe(5);
        config.TrafficPercentB.ShouldBe(20);
    }

    [Fact]
    public void ExtractAbTestConfig_Null_ReturnsNull()
    {
        AgentVersionRouter.ExtractAbTestConfig(null).ShouldBeNull();
    }

    [Fact]
    public void ExtractAbTestConfig_NoAbTestField_ReturnsNull()
    {
        AgentVersionRouter.ExtractAbTestConfig("""{"handoff":{"maxHandoffs":3}}""").ShouldBeNull();
    }

    [Fact]
    public void ExtractAbTestConfig_InvalidJson_ReturnsNull()
    {
        AgentVersionRouter.ExtractAbTestConfig("not-json").ShouldBeNull();
    }

    #endregion

    #region MergeAbTestConfig Tests

    [Fact]
    public void MergeAbTestConfig_AddToEmpty_ReturnsAbTestOnly()
    {
        var abTest = new AbTestConfig { Enabled = true, VersionA = 1, VersionB = 2, TrafficPercentB = 30 };

        var result = AgentVersionRouter.MergeAbTestConfig(null, abTest);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("abTest").GetProperty("enabled").GetBoolean().ShouldBeTrue();
        parsed.GetProperty("abTest").GetProperty("versionA").GetInt32().ShouldBe(1);
        parsed.GetProperty("abTest").GetProperty("versionB").GetInt32().ShouldBe(2);
        parsed.GetProperty("abTest").GetProperty("trafficPercentB").GetInt32().ShouldBe(30);
    }

    [Fact]
    public void MergeAbTestConfig_PreservesExistingFields()
    {
        var existing = """{"handoff":{"maxHandoffs":3},"router":{"allowDirectResponse":true}}""";
        var abTest = new AbTestConfig { Enabled = true, VersionA = 1, VersionB = 2, TrafficPercentB = 50 };

        var result = AgentVersionRouter.MergeAbTestConfig(existing, abTest);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("handoff").GetProperty("maxHandoffs").GetInt32().ShouldBe(3);
        parsed.GetProperty("router").GetProperty("allowDirectResponse").GetBoolean().ShouldBeTrue();
        parsed.GetProperty("abTest").GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void MergeAbTestConfig_RemoveAbTest_RemovesOnlyAbTest()
    {
        var existing = """{"handoff":{"maxHandoffs":3},"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";

        var result = AgentVersionRouter.MergeAbTestConfig(existing, null);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("handoff").GetProperty("maxHandoffs").GetInt32().ShouldBe(3);
        parsed.TryGetProperty("abTest", out _).ShouldBeFalse();
    }

    [Fact]
    public void MergeAbTestConfig_RemoveFromEmpty_ReturnsEmptyObject()
    {
        var result = AgentVersionRouter.MergeAbTestConfig(null, null);
        result.ShouldBe("{}");
    }

    #endregion

    #region AgentService A/B Test Methods

    [Fact]
    public async Task ConfigureAbTestAsync_AgentNotFound_Returns404()
    {
        var service = CreateAgentService(agents: []);

        var result = await service.ConfigureAbTestAsync(Guid.NewGuid(), new ConfigureAbTestDto
        {
            VersionA = 1, VersionB = 2, TrafficPercentB = 50
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);
    }

    [Fact]
    public async Task ConfigureAbTestAsync_SameVersions_Returns400()
    {
        var agentId = Guid.NewGuid();
        var agent = CreateAgent(agentId);
        var service = CreateAgentService(agents: [agent]);

        var result = await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto
        {
            VersionA = 1, VersionB = 1, TrafficPercentB = 50
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.InvalidContent);
    }

    [Fact]
    public async Task ConfigureAbTestAsync_VersionNotFound_Returns404()
    {
        var agentId = Guid.NewGuid();
        var agent = CreateAgent(agentId);
        var service = CreateAgentService(agents: [agent], versions: []);

        var result = await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto
        {
            VersionA = 1, VersionB = 2, TrafficPercentB = 50
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.AgentVersionNotFound);
    }

    [Fact]
    public async Task ConfigureAbTestAsync_ValidConfig_UpdatesConfiguration()
    {
        var agentId = Guid.NewGuid();
        var agent = CreateAgent(agentId);
        Agent? updatedAgent = null;

        var v1 = CreateVersionEntity(agentId, 1, "V1");
        var v2 = CreateVersionEntity(agentId, 2, "V2");

        var service = CreateAgentService(
            agents: [agent],
            versions: [v1, v2],
            onUpdate: a => updatedAgent = a);

        var result = await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto
        {
            VersionA = 1, VersionB = 2, TrafficPercentB = 30
        });

        result.Succeeded.ShouldBeTrue();
        updatedAgent.ShouldNotBeNull();

        var abConfig = AgentVersionRouter.ExtractAbTestConfig(updatedAgent.Configuration);
        abConfig.ShouldNotBeNull();
        abConfig.Enabled.ShouldBeTrue();
        abConfig.VersionA.ShouldBe(1);
        abConfig.VersionB.ShouldBe(2);
        abConfig.TrafficPercentB.ShouldBe(30);
    }

    [Fact]
    public async Task StopAbTestAsync_AgentNotFound_Returns404()
    {
        var service = CreateAgentService(agents: []);

        var result = await service.StopAbTestAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);
    }

    [Fact]
    public async Task StopAbTestAsync_RemovesAbTestConfig()
    {
        var agentId = Guid.NewGuid();
        var config = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
        var agent = CreateAgent(agentId, config);
        Agent? updatedAgent = null;

        var service = CreateAgentService(
            agents: [agent],
            onUpdate: a => updatedAgent = a);

        var result = await service.StopAbTestAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        updatedAgent.ShouldNotBeNull();
        updatedAgent.Configuration.ShouldBeNull();
    }

    [Fact]
    public async Task StopAbTestAsync_PreservesOtherConfig()
    {
        var agentId = Guid.NewGuid();
        var config = """{"handoff":{"maxHandoffs":3},"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";
        var agent = CreateAgent(agentId, config);
        Agent? updatedAgent = null;

        var service = CreateAgentService(
            agents: [agent],
            onUpdate: a => updatedAgent = a);

        var result = await service.StopAbTestAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        updatedAgent.ShouldNotBeNull();
        updatedAgent.Configuration.ShouldNotBeNull();
        var parsed = JsonSerializer.Deserialize<JsonElement>(updatedAgent.Configuration);
        parsed.GetProperty("handoff").GetProperty("maxHandoffs").GetInt32().ShouldBe(3);
        parsed.TryGetProperty("abTest", out _).ShouldBeFalse();
    }

    #endregion

    #region Helpers

    private static Agent CreateAgent(Guid? id = null, string? configuration = null)
    {
        return new Agent
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test Agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true,
            Configuration = configuration,
            TenantId = Guid.NewGuid()
        };
    }

    private static AgentVersion CreateVersionEntity(Guid agentId, int version, string name)
        => CreateVersionEntityWithPersona(agentId, version, name, personaId: null);

    private static AgentVersion CreateVersionEntityWithPersona(Guid agentId, int version, string name, Guid? personaId)
    {
        var snapshot = new
        {
            Name = name,
            Description = $"Description for {name}",
            Instructions = $"Instructions for {name}",
            Provider = "OpenAI",
            Model = "gpt-4o",
            ToolGroups = (List<string>?)null,
            Temperature = (double?)null,
            MaxTokens = (int?)null,
            TimeoutSeconds = (int?)null,
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single,
            Configuration = (string?)null,
            Domains = (List<string>?)null,
            Roles = (List<string>?)null,
            QualityTier = 3,
            LatencyTier = 3,
            CostTier = 3,
            PersonaId = personaId
        };

        return new AgentVersion
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Version = version,
            ConfigSnapshot = JsonSerializer.Serialize(snapshot),
            ChangeNote = $"Version {version}"
        };
    }

    private static AgentVersionRouter CreateRouter(List<AgentVersion>? versions = null)
    {
        var versionList = versions ?? [];
        var versionRepo = new Mock<IRepository<AgentVersion, Guid>>();

        // 设置 IQueryable 接口以支持 LINQ Where/FirstOrDefaultAsync
        var mock = versionList.BuildMock();
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Provider).Returns(mock.Provider);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Expression).Returns(mock.Expression);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());

        return new AgentVersionRouter(
            versionRepo.Object,
            Mock.Of<ILogger<AgentVersionRouter>>());
    }

    private static AgentService CreateAgentService(
        List<Agent>? agents = null,
        List<AgentVersion>? versions = null,
        Action<Agent>? onUpdate = null)
    {
        var agentList = agents ?? [];
        var versionList = versions ?? [];

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => agentList.FirstOrDefault(a => a.Id == id));

        agentRepo.Setup(r => r.UpdateAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .Callback<Agent, CancellationToken>((a, _) => onUpdate?.Invoke(a))
            .Returns(Task.CompletedTask);

        var versionRepo = new Mock<IRepository<AgentVersion, Guid>>();
        versionRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<AgentVersion, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<AgentVersion, bool>> predicate, CancellationToken _) =>
                versionList.AsQueryable().Any(predicate));

        // 设置 IQueryable 接口以支持 LINQ Where/OrderByDescending/Select/FirstOrDefaultAsync
        var versionMock = versionList.BuildMock();
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Provider).Returns(versionMock.Provider);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Expression).Returns(versionMock.Expression);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.ElementType).Returns(versionMock.ElementType);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.GetEnumerator()).Returns(versionMock.GetEnumerator());

        versionRepo.Setup(r => r.InsertAsync(It.IsAny<AgentVersion>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AgentService(
            agentRepo.Object,
            versionRepo.Object,
            Mock.Of<IAgentRuntime>(),
            new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider());
    }

    #endregion
}
