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
    public async Task ExecuteCommandAsync_TimedOut_KeepsOutputAlreadyCollected()
    {
        // Arrange: the command prints a diagnostic and then stops making progress.
        var handler = new MockDockerHandler();
        handler.SetupExecFlowThenHang(stdout: "step 1 done\n", stderr: "waiting for lock on /var/db\n");

        var sandbox = CreateSandbox(handler, commandTimeout: TimeSpan.FromMilliseconds(300));

        // Act
        var result = await sandbox.ExecuteCommandAsync("./migrate.sh");

        // Assert: the timeout notice is a prefix, not a replacement - a command that hung
        // mid-stream has usually already said why, and that is the whole diagnostic value.
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("timed out");
        result.Error.ShouldContain("waiting for lock on /var/db");
        result.Output.ShouldContain("step 1 done");
    }

    [Fact]
    public async Task ExecuteCommandAsync_WrapsCommandSoContainerKillsItOnTimeout()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler, commandTimeout: TimeSpan.FromSeconds(30));

        // Act
        await sandbox.ExecuteCommandAsync("./long-job.sh");

        // Assert: Docker has no kill-exec endpoint, so the container must kill the
        // process itself once we walk away - otherwise it keeps holding PidsLimit/cpu/memory.
        var wrapped = ExtractExecCommand(handler);
        wrapped.ShouldContain("timeout -s KILL");
        wrapped.ShouldContain("./long-job.sh");
    }

    [Fact]
    public async Task ExecuteCommandAsync_InContainerTimeoutOutlivesClientTimeout()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler, commandTimeout: TimeSpan.FromSeconds(30));

        // Act
        await sandbox.ExecuteCommandAsync("./long-job.sh");

        // Assert: the in-container timeout is a janitor running after we gave up, not a
        // second deadline. If it ever fired first the caller would get an exec exit code
        // instead of the documented timeout contract (exit -1 + "Command timed out").
        var seconds = ExtractInContainerTimeoutSeconds(handler);
        seconds.ShouldBeGreaterThan(30);
    }

    [Fact]
    public async Task ExecuteCommandAsync_RunsCommandUnchangedWhenImageHasNoTimeoutBinary()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler);

        // Act
        await sandbox.ExecuteCommandAsync("./long-job.sh");

        // Assert: an image without coreutils/busybox must not lose the ability to run
        // commands at all - it just keeps the old leak.
        var wrapped = ExtractExecCommand(handler);
        wrapped.ShouldContain("command -v timeout");
        wrapped.ShouldContain("else /bin/sh -c");
    }

    [Fact]
    public async Task ExecuteCommandAsync_DoesNotWrapWhenTheCommandHasNoTimeout()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler, commandTimeout: Timeout.InfiniteTimeSpan);

        // Act
        await sandbox.ExecuteCommandAsync("./long-job.sh");

        // Assert: "no timeout" is a deliberate choice by the caller (CancelAfter treats both
        // InfiniteTimeSpan and Zero as never-cancel). Inventing a deadline for the container would
        // kill commands that were meant to run unbounded.
        ExtractExecCommand(handler).ShouldBe("./long-job.sh");
    }

    [Fact]
    public async Task ExecuteCommandAsync_EscapesSingleQuotesWhenWrapping()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler);

        // Act: an unescaped quote would close the wrapper's quoting and let the rest of
        // the command run outside it.
        await sandbox.ExecuteCommandAsync("echo 'hello world'");

        // Assert
        var wrapped = ExtractExecCommand(handler);
        wrapped.ShouldContain("""echo '\''hello world'\''""");
    }

    [Fact]
    public async Task ExecuteCommandAsync_DeniedCommandIsRejectedBeforeWrapping()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("rm -rf /");

        // Assert: the wrapper must never be able to launder a blacklisted command past
        // the denial check by changing the string the matcher would have seen.
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("Command denied");
        handler.RequestLog.ShouldBeEmpty();
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

    /// <summary>
    /// Pull the shell command out of the exec-create request body Docker actually received.
    /// </summary>
    private static string ExtractExecCommand(MockDockerHandler handler)
    {
        var createRequest = handler.RequestLog.First(r =>
            r.Method == HttpMethod.Post && r.Url.Contains("/exec")
            && !r.Url.Contains("/start") && !r.Url.Contains("/json"));

        using var document = JsonDocument.Parse(createRequest.Body!);

        // The property name casing depends on the serializer defaults HttpClient applies;
        // Docker's Go decoder matches case-insensitively, so the test must too.
        var cmd = document.RootElement.EnumerateObject()
            .First(p => string.Equals(p.Name, "Cmd", StringComparison.OrdinalIgnoreCase));

        return cmd.Value[2].GetString()!;
    }

    private static int ExtractInContainerTimeoutSeconds(MockDockerHandler handler)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            ExtractExecCommand(handler), @"timeout -s KILL (\d+)");

        match.Success.ShouldBeTrue("expected the command to be wrapped in an in-container timeout");
        return int.Parse(match.Groups[1].Value);
    }

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
