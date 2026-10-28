using ForbiddenException = Tnzi.Exceptions.ForbiddenException;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 入队时由使用方的落点解析器决定运行时（执行目标 × CLI）、模型与工作目录，结果随运行记录保存。
/// </summary>
/// <remarks>
/// 每条「按落点」都配一条「没有解析器 / 解析器不表态 = 按绑定」的对照，否则把解析器整个忽略掉也一样绿。
/// </remarks>
public class CliRunPlacementEnqueueTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111a7");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222a7");

    private readonly Mock<ICliRunPlacementResolver> _resolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, AgentThread, Guid>>();

        // 进程内调用（没有登录用户）：请求里的模型原样采纳，测试只看落点
        _currentUser.Setup(m => m.Id).Returns((Guid?)null);
        services.AddScoped(_ => _currentUser.Object);
    }

    [Fact]
    public async Task APlacementOnAWorkspace_QueuesTheRunThere_WithItsModelAndWorkDirectory()
    {
        await SeedBindingAsync();
        CliRunPlacementRequest? seen = null;
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<CliRunPlacementRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CliRunPlacementRequest request, CancellationToken _) => seen = request)
            .ReturnsAsync(new CliRunPlacement
            {
                TargetId = "ws-alice",
                ProviderKey = "kimi",
                Model = "kimi-k3",
                WorkDirectory = " /home/alice/chat/t1 ",
                CreateWorkDirectory = true
            });

        var result = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x", Model = "requested" });

        result.Succeeded.ShouldBeTrue(result.Message);
        var run = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
        var runtime = await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync(r => r.Id == run.CliRuntimeId);
        runtime.HostId.ShouldBe("ws-alice");
        runtime.ProviderKey.ShouldBe("kimi");
        runtime.Mode.ShouldBe(CliRuntimeMode.RemoteDaemon);
        runtime.Status.ShouldBe(CliRuntimeStatus.Offline, "a target that never registered starts offline and gets activated");
        run.Model.ShouldBe("kimi-k3");
        run.AssignedWorkDirectory.ShouldBe("/home/alice/chat/t1");
        run.CreateAssignedWorkDirectory.ShouldBeTrue();

        seen.ShouldNotBeNull();
        seen.AgentId.ShouldBe(AgentId);
        seen.BindingRuntimeId.ShouldBe(RuntimeId);
        seen.BindingProviderKey.ShouldBe("claude");
        seen.RequestedModel.ShouldBe("requested");
    }

    [Fact]
    public async Task ASecondRunOnTheSameWorkspace_UsesTheSameRuntimeRow()
    {
        await SeedBindingAsync();
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<CliRunPlacementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliRunPlacement { TargetId = "ws-alice" });

        var first = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });
        var second = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "y" });

        var runtimes = await DbContext.Set<CliRun>().AsNoTracking()
            .Where(r => r.Id == first.Data || r.Id == second.Data)
            .Select(r => r.CliRuntimeId)
            .Distinct()
            .ToListAsync();
        var runtime = await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync(r => r.Id == runtimes.Single());
        runtime.ProviderKey.ShouldBe("claude", "no provider in the placement = the binding's");
    }

    [Fact]
    public async Task WithoutAResolver_OrWhenItHasNoOpinion_TheRunGoesToTheBindingsRuntime()
    {
        await SeedBindingAsync();
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<CliRunPlacementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliRunPlacement?)null);

        var withResolver = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x", Model = "m1" });
        var withoutResolver = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" }, resolver: false);

        foreach (var id in new[] { withResolver.Data, withoutResolver.Data })
        {
            var run = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == id);
            run.CliRuntimeId.ShouldBe(RuntimeId);
            run.AssignedWorkDirectory.ShouldBeNull();
        }

        (await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == withResolver.Data)).Model.ShouldBe("m1");
    }

    [Fact]
    public async Task APlacementNamingBothARuntimeAndATarget_IsRefused_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<CliRunPlacementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliRunPlacement { RuntimeId = RuntimeId, TargetId = "ws-alice" });

        var result = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AResolverThatRejectsTheTurn_PropagatesItsBusinessError_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<CliRunPlacementRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("This user has no workspace."));

        await Should.ThrowAsync<ForbiddenException>(() => EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" }));

        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ARuntimeWhoseCliVersionIsOutsideTheSupportedRange_IsRefusedAtEnqueue_WithTheReason()
    {
        await SeedBindingAsync(version: "2.0.77");

        var supported = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" }, resolver: false);
        supported.Succeeded.ShouldBeTrue("no range declared = any version runs");

        var refused = await EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" }, resolver: false, minimumVersion: "2.1.300");

        refused.Succeeded.ShouldBeFalse();
        refused.Code.ShouldBe(409);
        refused.ErrorCode.ShouldBe(ErrorCodes.CliRuntimeVersionUnsupported);
        refused.Message.ShouldNotBeNull().ShouldContain("2.0.77");
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, bool resolver = true, string? minimumVersion = null)
    {
        var options = new CliAgentOptions { Enabled = true };
        options.Providers["claude"] = new CliProviderOptions { MinimumVersion = minimumVersion };
        var monitor = new TestOptionsMonitor<CliAgentOptions>(options);

        var dispatcher = new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            monitor,
            ServiceProvider,
            providerRegistry: new CliProviderRegistry(monitor),
            placementResolver: resolver ? _resolver.Object : null,
            queueSignal: new CliRunQueueSignal());

        return await dispatcher.EnqueueAsync(request);
    }

    private async Task SeedBindingAsync(string? version = "2.1.310")
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            CliVersion = version,
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }
}
