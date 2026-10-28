namespace Tnzi.AI.Tests;

/// <summary>
/// A/B 分流键与模块其余身份判定同源：正在执行的请求的 UserId 优先，没有请求才看环境用户。
/// 后台子运行（spawn_agent）在父请求结束后执行，作用域里没有环境用户；管理端代为发起时环境用户是管理员。
/// 按环境用户分流，前者全部落到「按 Agent」的同一个变体，后者分进管理员的桶。
/// </summary>
public class AgentVersionRouterRequestUserTests
{
    private const string AbConfig = """{"abTest":{"enabled":true,"versionA":1,"versionB":2,"trafficPercentB":50}}""";

    public AgentVersionRouterRequestUserTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task RouteAsync_NoAmbientUser_SplitsByTheRequestUser()
    {
        var agentId = Guid.NewGuid();
        var variants = new HashSet<string>();

        for (var i = 0; i < 30; i++)
        {
            var accessor = new AgentExecutionContextAccessor
            {
                CurrentRequest = new AgentRunRequest { UserMessage = "x", UserId = Guid.NewGuid() }
            };
            var router = CreateRouter(agentId, ambientUserId: null, accessor);

            var result = await router.RouteAsync(CreateAgent(agentId), CancellationToken.None);

            result.IsAbTestRouted.ShouldBeTrue();
            variants.Add(result.SelectedVariant!);
        }

        variants.Count.ShouldBe(2, "background runs of different owners must not all land on one variant");
    }

    [Fact]
    public async Task RouteAsync_RequestUserDiffersFromAmbientUser_RoutesByTheRequestUser()
    {
        var agentId = Guid.NewGuid();
        var sawDifferentBuckets = false;

        for (var i = 0; i < 30; i++)
        {
            var owner = Guid.NewGuid();
            var admin = Guid.NewGuid();
            var ownerVariant = (await CreateRouter(agentId, owner, accessor: null)
                .RouteAsync(CreateAgent(agentId), CancellationToken.None)).SelectedVariant;
            var adminVariant = (await CreateRouter(agentId, admin, accessor: null)
                .RouteAsync(CreateAgent(agentId), CancellationToken.None)).SelectedVariant;
            sawDifferentBuckets |= ownerVariant != adminVariant;

            var accessor = new AgentExecutionContextAccessor
            {
                CurrentRequest = new AgentRunRequest { UserMessage = "x", UserId = owner }
            };
            var routed = await CreateRouter(agentId, admin, accessor).RouteAsync(CreateAgent(agentId), CancellationToken.None);

            routed.SelectedVariant.ShouldBe(ownerVariant, "an admin acting for the owner must not move the owner into the admin's bucket");
        }

        sawDifferentBuckets.ShouldBeTrue("the sample must contain owner/admin pairs that hash to different variants");
    }

    [Fact]
    public async Task RouteAsync_NoUserAnywhere_StaysStablePerAgent()
    {
        var agentId = Guid.NewGuid();
        string? first = null;

        for (var i = 0; i < 5; i++)
        {
            var router = CreateRouter(agentId, ambientUserId: null, new AgentExecutionContextAccessor());
            var result = await router.RouteAsync(CreateAgent(agentId), CancellationToken.None);
            first ??= result.SelectedVariant;
            result.SelectedVariant.ShouldBe(first);
        }
    }

    private static Agent CreateAgent(Guid agentId) => new()
    {
        Id = agentId,
        Name = "Original",
        Provider = "test",
        Configuration = AbConfig
    };

    private static AgentVersionRouter CreateRouter(Guid agentId, Guid? ambientUserId, IAgentExecutionContextAccessor? accessor)
    {
        var versions = new List<AgentVersion>
        {
            Version(agentId, 1, "Version A Agent"),
            Version(agentId, 2, "Version B Agent")
        };
        var versionRepo = new Mock<IRepository<AgentVersion, Guid>>();
        var mock = versions.BuildMock();
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Provider).Returns(mock.Provider);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.Expression).Returns(mock.Expression);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        versionRepo.As<IQueryable<AgentVersion>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());

        ICurrentUser? user = null;
        if (ambientUserId.HasValue)
        {
            var userMock = new Mock<ICurrentUser>();
            userMock.Setup(u => u.Id).Returns(ambientUserId.Value);
            user = userMock.Object;
        }

        return new AgentVersionRouter(
            versionRepo.Object,
            Mock.Of<ILogger<AgentVersionRouter>>(),
            user,
            executionContextAccessor: accessor);
    }

    private static AgentVersion Version(Guid agentId, int version, string name) => new()
    {
        Id = Guid.NewGuid(),
        AgentId = agentId,
        Version = version,
        ConfigSnapshot = JsonSerializer.Serialize(new { Name = name, Provider = "test", IsEnabled = true })
    };
}
