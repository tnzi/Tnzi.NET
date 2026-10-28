using System.Net;
using System.Text;

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
        handler.ExecCommands().Single()[^1].ShouldBe("./long-job.sh");
    }

    [Fact]
    public async Task ExecuteCommandAsync_CommandTravelsOnceAsItsOwnArgument()
    {
        // Arrange: the kernel caps every single argv string (MAX_ARG_STRLEN, 128 KiB on Linux).
        // A wrapper that embeds the command in both of its branches doubles what counts against it.
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);
        const string command = "echo 'it''s \"quoted\"' $HOME";

        // Act
        await sandbox.ExecuteCommandAsync(command);

        // Assert
        var argv = handler.ExecCommands().Single();
        argv.Count(a => a.Contains("echo")).ShouldBe(1);
        argv[^1].ShouldBe(command, "passed as a positional parameter, the command needs no re-quoting");
        argv[2].ShouldNotContain(command);
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
        handler.ExecCommands().Single().ShouldBe(["/bin/sh", "-c", "./long-job.sh"]);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WrapperRefersToTheCommandOnlyThroughItsParameter()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");

        var sandbox = CreateSandbox(handler);

        // Act: a quote in the command must not be able to close the wrapper's quoting
        // and let the rest of the command run outside it.
        await sandbox.ExecuteCommandAsync("echo 'hello world'; exit 3");

        // Assert: the script only ever says "$1"; the command is never spliced into it.
        var wrapped = ExtractExecCommand(handler);
        wrapped.ShouldContain("\"$1\"");
        wrapped.ShouldNotContain("hello world");
        handler.ExecCommands().Single()[^1].ShouldBe("echo 'hello world'; exit 3");
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
    public async Task ExecuteCommandAsync_CharacterSplitAcrossFrames_IsDecodedIntact()
    {
        // Arrange: Docker cuts frames wherever its pipe read stopped, so a 3-byte character
        // regularly straddles two frames. Decoding each frame on its own turns both halves
        // into U+FFFD, and str_replace would then write the damaged text back to disk.
        var text = "第一行中文\n第二行";
        var bytes = Encoding.UTF8.GetBytes(text);
        var cut = 4; // inside the second character (bytes 3..5)
        var handler = new MockDockerHandler();
        handler.SetupExecFlowWithFrames(0, (1, bytes[..cut]), (1, bytes[cut..]));

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("cat notes.md");

        // Assert
        result.ExitCode.ShouldBe(0);
        result.Output.ShouldBe(text);
    }

    [Fact]
    public async Task ExecuteCommandAsync_OutputOverLimit_SaysItWasTruncated()
    {
        // Arrange: 40 bytes of output against a 16-byte cap, delivered in two frames.
        var handler = new MockDockerHandler();
        handler.SetupExecFlowWithFrames(0,
            (1, Encoding.UTF8.GetBytes("0123456789abcdefghij")),
            (1, Encoding.UTF8.GetBytes("klmnopqrstuvwxyz!@#$")));

        var sandbox = CreateSandbox(handler, maxOutputSize: 16);

        // Act
        var result = await sandbox.ExecuteCommandAsync("cat big.log");

        // Assert: what was kept is the head of the stream, and the cut is announced.
        result.Output.ShouldStartWith("0123456789abcdef");
        result.Output.ShouldNotContain("ghij");
        result.Output.ShouldContain("truncated");
    }

    [Fact]
    public async Task ExecuteCommandAsync_LimitCountsBytesNotCharacters()
    {
        // Arrange: 8 three-byte characters = 24 bytes against a 12-byte cap, in one frame.
        var handler = new MockDockerHandler();
        handler.SetupExecFlowWithFrames(0, (1, Encoding.UTF8.GetBytes("一二三四五六七八")));

        var sandbox = CreateSandbox(handler, maxOutputSize: 12);

        // Act
        var result = await sandbox.ExecuteCommandAsync("cat cjk.txt");

        // Assert: four whole characters fit, no half character is emitted.
        result.Output.ShouldStartWith("一二三四");
        result.Output.ShouldNotContain("五");
        result.Output.ShouldNotContain("�");
        result.Output.ShouldContain("truncated");
    }

    [Fact]
    public async Task ReadFileAsync_ContentOverOutputLimit_ThrowsInsteadOfReturningPart()
    {
        // Arrange: the file is under the 50 MB read limit but over the exec output cap.
        var handler = new MockDockerHandler();
        handler.SetupExecFlowWithFrames(0,
            (1, Encoding.UTF8.GetBytes("0123456789abcdefghij")),
            (1, Encoding.UTF8.GetBytes("klmnopqrstuvwxyz!@#$")));

        var sandbox = CreateSandbox(handler, maxOutputSize: 16);

        // Act & Assert: a partial file returned as the whole one is how str_replace
        // and read-then-write_file end up writing a truncated file back.
        var ex = await Should.ThrowAsync<IOException>(() => sandbox.ReadFileAsync("/workspace/big.log"));
        ex.Message.ShouldContain("offset");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ExecInspectFails_IsNotReportedAsSuccess()
    {
        // Arrange
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        handler.SetupExecInspect(HttpStatusCode.InternalServerError, "daemon error");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("cat missing.txt");

        // Assert
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("exit status");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ExecInspectWithoutExitCode_IsNotReportedAsSuccess()
    {
        // Arrange: the daemon reports the exec as still running, with no exit code.
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        handler.SetupExecInspect(HttpStatusCode.OK, """{"ExitCode":null,"Running":true}""");

        var sandbox = CreateSandbox(handler);

        // Act
        var result = await sandbox.ExecuteCommandAsync("cat notes.md");

        // Assert
        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("exit status");
    }

    [Fact]
    public async Task ReadFileAsync_ExecInspectFails_ThrowsInsteadOfReturningEmptyContent()
    {
        // Arrange: cat produced nothing and its exit status cannot be read.
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        handler.SetupExecInspect(HttpStatusCode.InternalServerError, "daemon error");

        var sandbox = CreateSandbox(handler);

        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(() => sandbox.ReadFileAsync("/workspace/notes.md"));
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

    [Theory]
    [InlineData(60 * 1024)]
    [InlineData(200 * 1024)]
    [InlineData(1024 * 1024)]
    public async Task WriteFileAsync_LargeContent_StaysUnderTheArgumentLimitAndLandsByteForByte(int size)
    {
        // Arrange: the whole file used to travel base64-encoded inside one exec argument (twice, through
        // the timeout wrapper). Linux refuses any single argument over 128 KiB with E2BIG, so every file
        // over ~48 KB failed to write, and str_replace failed on every file that size.
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);
        var content = BuildText(size);

        // Act
        await sandbox.WriteFileAsync("/workspace/outputs/report.html", content);

        // Assert
        var argv = handler.ExecCommands();
        argv.SelectMany(a => a).ShouldAllBe(arg => Encoding.UTF8.GetByteCount(arg) < MaxArgStrlen);
        ReplayWrites(handler.ExecShellCommands())["/workspace/outputs/report.html"]
            .ShouldBe(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public async Task WriteFileAsync_AppendOfLargeContent_AppendsToTheTarget()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);
        var content = BuildText(300 * 1024);

        await sandbox.WriteFileAsync("/workspace/log.txt", content, append: true);

        var commands = handler.ExecShellCommands();
        commands.ShouldContain(c => c.Contains(">> '/workspace/log.txt'"));
        commands.ShouldNotContain(c => c.Contains("> '/workspace/log.txt'") && !c.Contains(">> '/workspace/log.txt'"));
        ReplayWrites(commands)["/workspace/log.txt"].ShouldBe(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public async Task UpdateFileAsync_LargeBinary_StaysUnderTheArgumentLimitAndLandsByteForByte()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);
        var bytes = new byte[400 * 1024];
        new Random(42).NextBytes(bytes);

        await sandbox.UpdateFileAsync("/workspace/outputs/report.xlsx", bytes);

        handler.ExecCommands().SelectMany(a => a).ShouldAllBe(arg => Encoding.UTF8.GetByteCount(arg) < MaxArgStrlen);
        ReplayWrites(handler.ExecShellCommands())["/workspace/outputs/report.xlsx"].ShouldBe(bytes);
    }

    [Fact]
    public async Task WriteFileAsync_LargeContent_StagingFailure_LeavesTheTargetUntouchedAndCleansUp()
    {
        // A chunk that fails to land must not truncate the file being replaced (str_replace writes the
        // whole file back): the target is only written once every chunk is staged.
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 1, stdout: "", stderr: "No space left on device");
        var sandbox = CreateSandbox(handler);

        await Should.ThrowAsync<IOException>(() => sandbox.WriteFileAsync("/workspace/big.txt", BuildText(300 * 1024)));

        var commands = handler.ExecShellCommands();
        commands.ShouldNotContain(c => c.Contains("'/workspace/big.txt'") && !c.StartsWith("mkdir"));
        commands[^1].ShouldStartWith("rm -f ");
    }

    [Fact]
    public async Task UpdateFileAsync_ContentWhoseBase64SpellsADeniedWord_IsStillWritten()
    {
        // The blacklist screens commands an agent asks for. Content crosses as base64, and "mkfs" turns up
        // case-insensitively in about one of every million base64 positions: held to the blacklist, most
        // one-megabyte writes were refused as "Command denied".
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);
        byte[] bytes = [154, 71, 236]; // base64: "mkfs"

        await sandbox.UpdateFileAsync("/workspace/data.bin", bytes);

        ReplayWrites(handler.ExecShellCommands())["/workspace/data.bin"].ShouldBe(bytes);
    }

    [Fact]
    public async Task ReadFileAsync_FileNameContainingADeniedWord_IsRead()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "notes");
        var sandbox = CreateSandbox(handler);

        var content = await sandbox.ReadFileAsync("/workspace/mkfs-notes.md");

        content.ShouldBe("notes");
    }

    [Fact]
    public async Task ExecuteCommandAsync_AgentCommandWithADeniedWord_IsStillDenied()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var sandbox = CreateSandbox(handler);

        var result = await sandbox.ExecuteCommandAsync("mkfs.ext4 /dev/sda1");

        result.ExitCode.ShouldBe(-1);
        result.Error.ShouldContain("Command denied");
        handler.RequestLog.ShouldBeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_RemovesTheContainerWithoutWaitingForAGracefulStop()
    {
        // The keep-alive PID 1 has no SIGTERM handler, so a graceful stop always waited its full
        // timeout before the kill, holding every run's reply and its container slot that long.
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/container-123", HttpStatusCode.NoContent, "", HttpMethod.Delete);
        var released = 0;
        var sandbox = CreateSandbox(handler, onDisposed: () => released++);

        await sandbox.DisposeAsync();

        handler.RequestLog.ShouldNotContain(r => r.Url.Contains("/stop"));
        handler.RequestLog.ShouldContain(r => r.Method == HttpMethod.Delete && r.Url.Contains("/containers/container-123?force=true"));
        released.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_ContainerAlreadyRemoved_IsNotAWarning()
    {
        var handler = new MockDockerHandler();
        handler.SetupResponse("/containers/container-123", HttpStatusCode.NotFound, """{"message":"No such container"}""", HttpMethod.Delete);
        var logger = new CapturingLogger();
        var sandbox = CreateSandbox(handler, logger: logger);

        await sandbox.DisposeAsync();

        logger.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task DisposeAsync_RemovalThrows_StillReleasesTheSlot()
    {
        var released = 0;
        var sandbox = CreateSandbox(new ThrowingHandler(), onDisposed: () => released++);

        await sandbox.DisposeAsync();

        released.ShouldBe(1);
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

    private static DockerSandbox CreateSandbox(HttpMessageHandler handler,
        TimeSpan? commandTimeout = null, long maxOutputSize = 512 * 1024,
        Action? onDisposed = null, ILogger? logger = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/v1.45") };
        return new DockerSandbox(
            id: "docker-test",
            httpClient: httpClient,
            containerId: "container-123",
            workspacePath: "/workspace",
            commandTimeout: commandTimeout ?? TimeSpan.FromSeconds(30),
            maxOutputSize: maxOutputSize,
            logger: logger ?? NullLogger.Instance,
            onDisposed: onDisposed);
    }

    /// <summary>Linux <c>MAX_ARG_STRLEN</c>: no single argv string may exceed 32 pages.</summary>
    private const int MaxArgStrlen = 128 * 1024;

    /// <summary>Text of roughly <paramref name="size"/> UTF-8 bytes with multi-byte characters and quotes in it.</summary>
    private static string BuildText(int size)
    {
        var sb = new StringBuilder(size);
        var line = 0;
        while (Encoding.UTF8.GetByteCount(sb.ToString()) < size)
            sb.Append($"<p data-line=\"{line++}\">l'été 报告 % $HOME `x` \\ 🙂</p>\n");
        return sb.ToString();
    }

    /// <summary>
    /// Replay the file writes a sequence of exec commands performs: base64 piped into a target, base64
    /// text staged into a file in parts and then decoded into a target, and removals. Returns the bytes
    /// each target ends up with.
    /// </summary>
    private static Dictionary<string, byte[]> ReplayWrites(IEnumerable<string> commands)
    {
        var staged = new Dictionary<string, StringBuilder>();
        var files = new Dictionary<string, List<byte>>();

        foreach (var command in commands)
        {
            var piped = System.Text.RegularExpressions.Regex.Match(command,
                @"^(?:printf '%s'|echo) '([A-Za-z0-9+/=]*)' \| base64 -d (>>?) '([^']+)'$");
            if (piped.Success)
            {
                Write(piped.Groups[3].Value, Convert.FromBase64String(piped.Groups[1].Value), piped.Groups[2].Value == ">>");
                continue;
            }

            var stage = System.Text.RegularExpressions.Regex.Match(command,
                @"^printf '%s' '([A-Za-z0-9+/=]*)' (>>?) '([^']+)'$");
            if (stage.Success)
            {
                var path = stage.Groups[3].Value;
                if (stage.Groups[2].Value == ">" || !staged.ContainsKey(path)) staged[path] = new StringBuilder();
                staged[path].Append(stage.Groups[1].Value);
                continue;
            }

            var decode = System.Text.RegularExpressions.Regex.Match(command, @"^base64 -d '([^']+)' (>>?) '([^']+)'$");
            if (decode.Success)
            {
                Write(decode.Groups[3].Value, Convert.FromBase64String(staged[decode.Groups[1].Value].ToString()), decode.Groups[2].Value == ">>");
                continue;
            }

            var remove = System.Text.RegularExpressions.Regex.Match(command, @"^rm -f '([^']+)'$");
            if (remove.Success)
            {
                staged.Remove(remove.Groups[1].Value);
            }
        }

        return files.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

        void Write(string path, byte[] bytes, bool append)
        {
            if (!append || !files.ContainsKey(path)) files[path] = [];
            files[path].AddRange(bytes);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Docker daemon is not reachable");
    }

    #endregion
}
