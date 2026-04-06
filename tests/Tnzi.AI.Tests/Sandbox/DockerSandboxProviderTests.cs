using System.Net;

namespace Tnzi.AI.Tests.Sandbox;

public class DockerSandboxProviderTests
{
    private const string ContainerCreateResponse = """{"Id":"abc123def456","Warnings":[]}""";

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
            WorkspacePath = "/tmp/test1"
        });

        // Act & Assert: second should timeout
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = "/tmp/test2"
            }));
        ex.Message.ShouldContain("Maximum number of concurrent containers");

        await sandbox1.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_StopsAndRemovesContainer()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456/stop", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.NoContent, "", HttpMethod.Delete);

        var provider = CreateProvider(handler);
        var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Act
        await provider.DisposeAsync(sandbox);

        // Assert
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Post && r.Url.Contains("/stop"));
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Delete && r.Url.Contains("/containers/"));
    }

    [Fact]
    public async Task DisposeAsync_ContinuesCleanupWhenStopFails()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456/stop", HttpStatusCode.InternalServerError, "error");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.NoContent, "", HttpMethod.Delete);

        var provider = CreateProvider(handler);
        var sandbox = await provider.CreateAsync(CreateSandboxOptions());

        // Act — should not throw even though stop failed
        await provider.DisposeAsync(sandbox);

        // Assert: remove was still called despite stop failure
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesContainerSlot()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456/stop", HttpStatusCode.NoContent, "");
        handler.SetupResponse("/containers/abc123def456", HttpStatusCode.NoContent, "", HttpMethod.Delete);

        var provider = CreateProvider(handler, opts => opts.Docker.MaxContainers = 1);

        // Create and dispose
        var sandbox1 = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = "/tmp/test1"
        });
        await provider.DisposeAsync(sandbox1);

        // Act: should succeed since slot was released
        var sandbox2 = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = "/tmp/test2"
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
    public async Task CreateAsync_MountsWorkspaceVolume()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/create", HttpStatusCode.Created, ContainerCreateResponse);
        handler.SetupResponse("/containers/abc123def456/start", HttpStatusCode.NoContent, "");

        var provider = CreateProvider(handler);

        // Act
        await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = Guid.NewGuid(),
            WorkspacePath = "/home/user/workspace"
        });

        // Assert
        var createRequest = handler.RequestLog.First(r => r.Url.Contains("/containers/create"));
        var body = createRequest.Body;
        body.ShouldNotBeNull();
        body.ShouldContain("/home/user/workspace:/workspace");
    }

    #region Helpers

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

    private static SandboxCreateOptions CreateSandboxOptions() => new()
    {
        ThreadId = Guid.NewGuid(),
        WorkspacePath = "/tmp/test-workspace"
    };

    #endregion
}
