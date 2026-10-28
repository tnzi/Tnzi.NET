using System.Net;

namespace Tnzi.AI.Tests.Sandbox;

public class DockerSandboxProviderTests : IDisposable
{
    private const string ContainerCreateResponse = """{"Id":"abc123def456","Warnings":[]}""";

    // The provider creates the bind sources on the host, so every workspace path lives under a temp root.
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tnzi-docker-provider-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Name_ReturnsDocker()
    {
        var provider = CreateProvider();
        provider.Name.ShouldBe("docker");
    }

    [Fact]
    public async Task CreateAsync_CreatesAndStartsContainer()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler);
        var options = CreateSandboxOptions();

        // Act
        await using var sandbox = await provider.CreateAsync(options);

        // Assert
        sandbox.ShouldNotBeNull();
        sandbox.Id.ShouldStartWith("docker-");
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Post && r.Url.Contains("/containers/create"));
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Post && r.Url.Contains("/start"));
    }

    [Fact]
    public async Task CreateAsync_SetsResourceLimits()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler, opts =>
        {
            opts.Docker.MemoryLimitMb = 256;
            opts.Docker.CpuLimit = 0.5;
        });

        // Act
        await using var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Assert
        var createRequest = handler.RequestLog.First(r => r.Url.Contains("/containers/create"));
        createRequest.Body.ShouldNotBeNull();
        // NanoCPUs = 0.5 * 1e9 = 500000000
        createRequest.Body.ShouldContain("500000000");
        // Memory = 256 * 1024 * 1024 = 268435456
        createRequest.Body.ShouldContain("268435456");
    }

    [Fact]
    public async Task CreateAsync_FailsWhenContainerCreationFails()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.InternalServerError, """{"message":"no such image"}""");

        var provider = CreateProvider(handler);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.CreateAsync(CreateSandboxOptions()));
        ex.Message.ShouldContain("Failed to create Docker container");
    }

    [Fact]
    public async Task CreateAsync_FailsWhenStartFails()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.InternalServerError, """{"message":"start error"}""");

        var provider = CreateProvider(handler);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => provider.CreateAsync(CreateSandboxOptions()));
    }

    [Fact]
    public async Task CreateAsync_RespectsMaxContainerLimit()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler, opts => opts.Docker.MaxContainers = 1);

        // Create first sandbox (should succeed)
        var sandbox1 = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = Workspace("test1")
        });

        // Act & Assert: second should timeout
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = Workspace("test2")
            }));
        ex.Message.ShouldContain("Maximum number of concurrent containers");

        await sandbox1.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ForceRemovesContainerWithoutAGracefulStop()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.NoContent, "", HttpMethod.Delete);

        var provider = CreateProvider(handler);
        var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Act
        await provider.DisposeAsync(sandbox);

        // Assert: PID 1 ignores SIGTERM, so a graceful stop only ever waited out its timeout.
        handler.RequestLog.ShouldNotContain(r => r.Url.Contains("/stop"));
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Delete && r.Url.Contains("/containers/abc123def456?force=true"));
    }

    [Fact]
    public async Task DisposeAsync_RemovalFails_StillReleasesTheSlot()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.InternalServerError, "error", HttpMethod.Delete);

        var provider = CreateProvider(handler, opts => opts.Docker.MaxContainers = 1);
        var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Act - should not throw even though the removal failed
        await provider.DisposeAsync(sandbox);

        // Assert: the slot is free again
        await using var next = await provider.CreateAsync(CreateSandboxOptions());
        next.ShouldNotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesContainerSlot()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.NoContent, "", HttpMethod.Delete);

        var provider = CreateProvider(handler, opts => opts.Docker.MaxContainers = 1);

        // Create and dispose
        var sandbox1 = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = Workspace("test1")
        });
        await provider.DisposeAsync(sandbox1);

        // Act: should succeed since slot was released
        var sandbox2 = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = Workspace("test2")
        });

        sandbox2.ShouldNotBeNull();
        await sandbox2.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_ContainerHasSecurityLabels()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler);

        // Act
        await using var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Assert
        var createRequest = handler.RequestLog.First(r => r.Url.Contains("/containers/create"));
        createRequest.Body.ShouldNotBeNull();
        createRequest.Body.ShouldContain("tnzi.sandbox");
        createRequest.Body.ShouldContain("no-new-privileges");
        createRequest.Body.ShouldContain("none"); // NetworkMode
    }

    [Fact]
    public async Task CreateAsync_MountsTheThreadDirectoryAndEachMntPath()
    {
        // Arrange: agents write /mnt/outputs/... into scripts and file contents, not only on the bash
        // command line, so the virtual namespace has to exist inside the container.
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler);
        var threadDir = Workspace("thread");

        // Act
        await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = threadDir
        });

        // Assert
        var binds = ExtractBinds(handler);
        binds.ShouldContain($"{threadDir}:/workspace");
        foreach (var mount in new[] { "workspace", "uploads", "outputs" })
        {
            binds.ShouldContain($"{Path.Combine(threadDir, mount)}:/mnt/{mount}");
        }

        // 技能副本归本进程所有，容器只读：两个入口都是只读挂载
        binds.ShouldContain($"{Path.Combine(threadDir, "skills")}:/mnt/skills:ro");
        binds.ShouldContain($"{Path.Combine(threadDir, "skills")}:/workspace/skills:ro");
    }

    [Fact]
    public async Task CreateAsync_CreatesEveryBindSourceBeforeTheContainer()
    {
        // A missing bind source is created by the Docker daemon, as root: the non-root sandbox user
        // could then never write into it.
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        var provider = CreateProvider(handler);
        var threadDir = Workspace("lazy-thread");

        await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = threadDir
        });

        foreach (var mount in new[] { "workspace", "uploads", "outputs", "skills" })
        {
            Directory.Exists(Path.Combine(threadDir, mount)).ShouldBeTrue(mount);
        }
    }

    [Fact]
    public async Task CreateAsync_NameHeldByAnOrphanOfThisThread_RemovesItAndRetries()
    {
        // A process that died while a run held the container left it running; the name is derived
        // from the thread id, so every later sandbox of that thread hit 409 Conflict for good.
        var threadId = Guid.NewGuid();
        var handler = new MockDockerHandler();
        handler.SetupResponseSequence("/containers/create", HttpMethod.Post,
            (HttpStatusCode.Conflict, """{"message":"Conflict. The container name is already in use"}"""),
            (HttpStatusCode.Created, ContainerCreateResponse));
        handler.SetupResponse($"/containers/tnzi-sandbox-{threadId:N}/json", HttpStatusCode.OK,
            OrphanInspect("orphan999", threadId), HttpMethod.Get);
        handler.SetupResponse("/containers/orphan999", HttpStatusCode.NoContent, "", HttpMethod.Delete);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        var provider = CreateProvider(handler);

        await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = threadId,
            WorkspacePath = Workspace("orphaned")
        });

        sandbox.ShouldNotBeNull();
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Delete && r.Url.Contains("/containers/orphan999?force=true"));
        handler.RequestLog.Count(r => r.Url.Contains("/containers/create")).ShouldBe(2);
    }

    [Fact]
    public async Task CreateAsync_NameHeldByAContainerThatIsNotThisThreadsSandbox_FailsWithoutRemovingIt()
    {
        var threadId = Guid.NewGuid();
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Conflict, """{"message":"Conflict"}""");
        handler.SetupResponse($"/containers/tnzi-sandbox-{threadId:N}/json", HttpStatusCode.OK,
            """{"Id":"foreign1","Config":{"Labels":{"app":"something-else"}}}""", HttpMethod.Get);
        var provider = CreateProvider(handler);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = threadId,
            WorkspacePath = Workspace("foreign")
        }));

        ex.Message.ShouldContain($"tnzi-sandbox-{threadId:N}");
        handler.RequestLog.ShouldNotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task CreateAsync_ThreadAlreadyHasASandboxInThisProcess_FailsWithoutTouchingDocker()
    {
        // The live container of a concurrent run in this process must never be taken for an orphan.
        var threadId = Guid.NewGuid();
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        var provider = CreateProvider(handler);
        var options = new SandboxCreateOptions { ThreadId = threadId, WorkspacePath = Workspace("busy") };

        await using var first = await provider.CreateAsync(options);
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(options));

        ex.Message.ShouldContain("already");
        handler.RequestLog.Count(r => r.Url.Contains("/containers/create")).ShouldBe(1);
        handler.RequestLog.ShouldNotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task CreateAsync_FailedCreate_DoesNotLeaveTheThreadMarkedBusy()
    {
        var threadId = Guid.NewGuid();
        var handler = new MockDockerHandler();
        handler.SetupResponseSequence("/containers/create", HttpMethod.Post,
            (HttpStatusCode.InternalServerError, "boom"),
            (HttpStatusCode.Created, ContainerCreateResponse));
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        var provider = CreateProvider(handler);
        var options = new SandboxCreateOptions { ThreadId = threadId, WorkspacePath = Workspace("retry") };

        await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(options));
        await using var sandbox = await provider.CreateAsync(options);

        sandbox.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateAsync_StartFails_RemovesTheContainerItJustCreated()
    {
        // ★ 容器名按 threadId 派生（tnzi-sandbox-{threadId}）而 Docker 的容器名全局唯一：
        // 创建成功但启动失败时留下的死容器，会让这个 thread 之后每一次创建都撞 409 Conflict —— 
        // 而它自己一次都没成功跑起来过。
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.InternalServerError, "boom");
        handler.SetupResponse("/containers/abc123def456?force=true", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler);

        await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(CreateSandboxOptions()));

        handler.RequestLog.ShouldContain(r =>
            r.Method == HttpMethod.Delete && r.Url.Contains("/containers/abc123def456"));
    }

    [Fact]
    public async Task CreateAsync_CreateItselfFails_DoesNotAttemptARemoval()
    {
        // 对照组：容器根本没建出来时不该去删一个不存在的 id。
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.InternalServerError, "nope");

        var provider = CreateProvider(handler);

        await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(CreateSandboxOptions()));

        handler.RequestLog.ShouldNotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task CreateAsync_StartFails_ReleasesTheConcurrencySlot()
    {
        // 槽位不释放的话，几次失败的创建就把 MaxContainers 耗光，之后连成功的创建也起不来。
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.InternalServerError, "boom");
        handler.SetupResponse("/containers/abc123def456?force=true", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler, opts => opts.Docker.MaxContainers = 1);

        await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(CreateSandboxOptions()));

        // 第二次仍然拿得到槽位（否则会在 30 秒等待后抛"达到并发上限"）。
        await Should.ThrowAsync<InvalidOperationException>(() => provider.CreateAsync(CreateSandboxOptions()));
        handler.RequestLog.Count(r => r.Url.Contains("/containers/create")).ShouldBe(2);
    }

    #region Helpers

    private string Workspace(string name) => Path.Combine(_root, name);

    private static string OrphanInspect(string id, Guid threadId) =>
        JsonSerializer.Serialize(new
        {
            Id = id,
            Config = new { Labels = new Dictionary<string, string> { ["tnzi.sandbox"] = "true", ["tnzi.sandbox.thread"] = threadId.ToString("N") } }
        });

    private static List<string> ExtractBinds(MockDockerHandler handler)
    {
        var createRequest = handler.RequestLog.First(r => r.Url.Contains("/containers/create"));
        using var document = JsonDocument.Parse(createRequest.Body!);
        var hostConfig = document.RootElement.EnumerateObject()
            .First(p => string.Equals(p.Name, "HostConfig", StringComparison.OrdinalIgnoreCase)).Value;
        var binds = hostConfig.EnumerateObject()
            .First(p => string.Equals(p.Name, "Binds", StringComparison.OrdinalIgnoreCase)).Value;
        return binds.EnumerateArray().Select(b => b.GetString()!).ToList();
    }

    private static DockerSandboxProvider CreateProvider(MockDockerHandler? handler = null,
        Action<SandboxModuleOptions>? configure = null)
    {
        var options = new SandboxModuleOptions();
        configure?.Invoke(options);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(DockerSandboxProvider.HttpClientName))
            .Returns(() =>
            {
                var client = handler is not null
                    ? new HttpClient(handler) { BaseAddress = new Uri("http://localhost/v1.45") }
                    : new HttpClient { BaseAddress = new Uri("http://localhost/v1.45") };
                return client;
            });

        return new DockerSandboxProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            mockFactory.Object,
            NullLogger<DockerSandboxProvider>.Instance);
    }

    private SandboxCreateOptions CreateSandboxOptions() => new()
    {
        ThreadId = Guid.NewGuid(),
        WorkspacePath = Workspace($"thread-{Guid.NewGuid():N}")
    };

    #endregion
}
