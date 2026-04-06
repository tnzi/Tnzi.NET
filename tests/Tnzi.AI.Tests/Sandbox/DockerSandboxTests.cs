using System.Net;

namespace Tnzi.AI.Tests.Sandbox;

public class DockerSandboxTests
{
    [Fact]
    public async Task ExecuteCommandAsync_SendsExecCreateAndStart()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "hello world");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("echo hello world");

        // Assert
        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("hello world");
        handler.RequestLog.ShouldContain(r => r.Url.Contains("/exec"));
    }

    [Fact]
    public async Task ExecuteCommandAsync_ReturnsNonZeroExitCode()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 127, stdout: "", stderr: "command not found");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("nonexistent-cmd");

        // Assert
        result.ExitCode.ShouldBe(127);
        result.Error.ShouldContain("command not found");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ReturnsErrorOnExecCreateFailure()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/", HttpStatusCode.InternalServerError, "container not running");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("echo test");

        // Assert
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("Failed to create exec instance");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ReturnsTimeoutOnCancellation()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupDelay(TimeSpan.FromSeconds(30));

        var sandbox = CreateSandbox(handler, commandTimeout: TimeSpan.FromMilliseconds(100));

        // Act
        var result = await sandbox.ExecuteCommandAsync("sleep 30");

        // Assert
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("timed out");
    }

    [Fact]
    public async Task ReadFileAsync_UsesExecCat()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "file content here");

        var sandbox = CreateSandbox(handler);

        // Act
        var content = await sandbox.ReadFileAsync("/workspace/test.txt");

        // Assert
        content.ShouldContain("file content here");
    }

    [Fact]
    public async Task ReadFileAsync_ThrowsOnFileNotFound()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 1, stdout: "", stderr: "No such file or directory");

        var sandbox = CreateSandbox(handler);

        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(
            () => sandbox.ReadFileAsync("/workspace/missing.txt"));
    }

    [Fact]
    public async Task WriteFileAsync_CreatesDirectoryAndWritesFile()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler);

        // Act & Assert: should not throw
        await sandbox.WriteFileAsync("/workspace/sub/test.txt", "test content");

        // Verify exec calls were made (mkdir + write)
        handler.RequestLog.Count(r => r.Url.Contains("/exec") && r.Method == HttpMethod.Post
            && !r.Url.Contains("/start") && !r.Url.Contains("/json")).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListDirectoryAsync_ParsesFindOutput()
    {
        // Arrange
        var findOutput = "d\t4096\t/workspace/src\nf\t1234\t/workspace/test.txt\n";
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: findOutput);

        var sandbox = CreateSandbox(handler);

        // Act
        var entries = await sandbox.ListDirectoryAsync("/workspace");

        // Assert
        entries.Count.ShouldBe(2);
        entries.ShouldContain(e => e.Name == "src" && e.IsDirectory);
        entries.ShouldContain(e => e.Name == "test.txt" && !e.IsDirectory && e.Size == 1234);
    }

    [Fact]
    public async Task DisposeAsync_PreventsSubsequentOperations()
    {
        // Arrange
        var handler = new MockDockerHandler();
        var sandbox = CreateSandbox(handler);

        // Act
        await sandbox.DisposeAsync();

        // Assert
        await Should.ThrowAsync<ObjectDisposedException>(
            () => sandbox.ExecuteCommandAsync("echo test"));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        var handler = new MockDockerHandler();
        var sandbox = CreateSandbox(handler);

        // Act & Assert: double dispose should not throw
        await sandbox.DisposeAsync();
        await sandbox.DisposeAsync();
    }

    #region Helpers

    private static DockerSandbox CreateSandbox(MockDockerHandler handler,
        TimeSpan? commandTimeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/v1.45") };
        return new DockerSandbox(
            id: "docker-test",
            httpClient: httpClient,
            containerId: "container-123",
            workspacePath: "/workspace",
            commandTimeout: commandTimeout ?? TimeSpan.FromSeconds(30),
            maxOutputSize: 512 * 1024,
            logger: NullLogger.Instance);
    }

    #endregion
}
