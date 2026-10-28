using System.Collections.Concurrent;

namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Docker container sandbox provider - creates and manages Docker containers
/// via the Docker Engine REST API (no Docker.DotNet dependency).
/// Supports both Unix socket (Linux/macOS) and named pipe (Windows) connections.
/// </summary>
public class DockerSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DockerSandboxProvider> _logger;
    private readonly IShellCommandAnalyzer? _commandAnalyzer;
    // 本进程里每个线程正在用的沙箱（sandboxId → 容器 id，创建完成前为空串）。创建前先占位：
    // 同一线程同时只能有一个沙箱（容器名按线程派生），名字被占时据此区分「本进程在用」与「上次没收尾」。
    private readonly ConcurrentDictionary<string, string> _activeContainers = new();
    private readonly SemaphoreSlim _containerSemaphore;
    private int _notWritableWarned;

    public const string HttpClientName = "DockerEngine";

    public string Name => "docker";

    public DockerSandboxProvider(IOptions<SandboxModuleOptions> options,
        IHttpClientFactory httpClientFactory, ILogger<DockerSandboxProvider> logger,
        IShellCommandAnalyzer? commandAnalyzer = null)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
        _commandAnalyzer = commandAnalyzer;
        _containerSemaphore = new SemaphoreSlim(_options.Value.Docker.MaxContainers);
    }

    public async Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        Check.NotNull(options);
        var dockerOpts = _options.Value.Docker;

        if (!await _containerSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            throw new InvalidOperationException(
                $"Maximum number of concurrent containers ({dockerOpts.MaxContainers}) reached");
        }

        var sandboxId = $"docker-{options.ThreadId:N}";
        var containerName = $"tnzi-sandbox-{options.ThreadId:N}";

        // 先占住这个线程：容器名按线程派生，同一进程里同一线程同时只能有一个沙箱。占位也是孤儿判定的依据 ——
        // 名字被占而本进程没有占位时，那个容器不属于本进程里任何一次在跑的运行（见 RemoveOrphanAsync）。
        if (!_activeContainers.TryAdd(sandboxId, string.Empty))
        {
            _containerSemaphore.Release();
            throw new InvalidOperationException(
                $"A sandbox container for thread {options.ThreadId:N} is already in use by another run in this process. " +
                "Runs of one thread share a Docker sandbox one at a time.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        string? createdContainerId = null;
        try
        {
            PrepareBindSources(options.WorkspacePath, dockerOpts);

            // 创建容器；名字被上次没收尾的容器占着时（进程崩溃、删除失败）清掉它再建一次
            var containerId = await CreateContainerAsync(httpClient, containerName, options, dockerOpts, ct);
            if (containerId is null)
            {
                await RemoveOrphanAsync(httpClient, containerName, options.ThreadId, ct);
                containerId = await CreateContainerAsync(httpClient, containerName, options, dockerOpts, ct)
                    ?? throw new InvalidOperationException(
                        $"Failed to create Docker container: the name {containerName} is still in use after its previous container was removed.");
            }

            createdContainerId = containerId;

            // 启动容器
            await StartContainerAsync(httpClient, containerId, ct);

            _activeContainers[sandboxId] = containerId;

            _logger.LogInformation("Docker sandbox {SandboxId} created (container: {ContainerId}, image: {Image})",
                sandboxId, containerId[..12], dockerOpts.Image);

            return new DockerSandbox(
                id: sandboxId,
                httpClient: httpClient,
                containerId: containerId,
                workspacePath: DockerBindSources.ThreadMountPoint,
                commandTimeout: options.CommandTimeout,
                maxOutputSize: options.MaxOutputSizeBytes,
                logger: _logger,
                deniedCommands: dockerOpts.DeniedCommands,
                onDisposed: () =>
                {
                    // Provider-side cleanup invoked by sandbox.DisposeAsync():
                    // release the slot AND remove from active map. Idempotent because
                    // DockerSandbox.DisposeAsync guards with _disposed flag.
                    _activeContainers.TryRemove(sandboxId, out _);
                    _containerSemaphore.Release();
                },
                deniedCommandPrefixes: dockerOpts.DeniedCommandPrefixes,
                commandAnalyzer: _commandAnalyzer,
                deniedPatterns: dockerOpts.DeniedPatterns,
                maxFileSize: options.MaxFileSizeBytes,
                // 容器侧路径视图的换根依据：Binds 把这个目录挂在 /workspace、子目录挂在 /mnt/*（DockerBindSources）。
                hostWorkspacePath: options.WorkspacePath);
        }
        catch
        {
            // ★ 创建成功但启动失败时必须删掉容器。容器名按 threadId 派生
            // （tnzi-sandbox-{threadId}），Docker 的容器名是全局唯一的：留一个死容器在那里，
            // 这个 thread 之后每一次创建都撞 409 Conflict，而它自己一次都没成功跑起来过。
            // 删除失败只记日志：那时原始异常才是要抛给调用方的那个。
            if (createdContainerId is not null)
            {
                await TryRemoveContainerAsync(httpClient, createdContainerId, sandboxId);
            }

            _activeContainers.TryRemove(sandboxId, out _);
            _containerSemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 建出挂载源，并在原生 Linux 上让容器用户能写进去（见 <see cref="DockerBindSources"/>）。
    /// </summary>
    private void PrepareBindSources(string threadDirectory, DockerSandboxOptions dockerOpts)
    {
        DockerBindSources.EnsureExist(threadDirectory);

        var container = DockerBindSources.ParseNumericIdentity(dockerOpts.RunAsUser);
        switch (DockerBindSources.DecideOwnership(OperatingSystem.IsLinux(), DockerBindSources.CurrentProcessUid, container))
        {
            case BindOwnershipAction.HandOverToContainerUser:
                foreach (var source in DockerBindSources.WritableSources(threadDirectory))
                {
                    try
                    {
                        DockerBindSources.HandOver(source, container!.Value);
                    }
                    catch (IOException ex)
                    {
                        // 例如 root_squash 的 NFS：改不了属主不挡沙箱（读与 /tmp 照常可用），但要说出来写不进去的原因。
                        _logger.LogWarning(ex,
                            "Docker sandbox: could not hand {Path} to the container user {RunAsUser}; sandboxed commands may not be able to write there",
                            source, dockerOpts.RunAsUser);
                    }
                }

                break;

            case BindOwnershipAction.WarnNotWritable:
                if (Interlocked.Exchange(ref _notWritableWarned, 1) == 0)
                {
                    _logger.LogWarning(
                        "Docker sandbox containers run as {RunAsUser}, but this process runs as uid {ProcessUid} and owns the thread " +
                        "directories it mounts into them, so sandboxed commands cannot write to /mnt/workspace, /mnt/uploads or " +
                        "/mnt/outputs. Set AI:Sandbox:Docker:RunAsUser to this process's non-root uid:gid, or run the application as {RunAsUser}.",
                        dockerOpts.RunAsUser, DockerBindSources.CurrentProcessUid, dockerOpts.RunAsUser);
                }

                break;
        }
    }

    /// <summary>
    /// 容器名被占时，确认占着它的是本线程上次没收尾的沙箱容器，然后删掉它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 容器名按线程派生，进程在运行持有容器时被杀掉（OOM、kill -9、驱逐、重启部署）后，那个容器以
    /// <c>tail -f /dev/null</c> 一直跑下去（<c>AutoRemove</c> 只在容器退出时触发），这个线程之后每一次要沙箱都撞
    /// 409，永久不可用，直到有人手工 <c>docker rm</c>。
    /// </para>
    /// <para>
    /// 只删带着本线程沙箱标签的容器；调用前本进程已为这个线程占位，所以它不是本进程里任何一次在跑的运行。
    /// 多个应用进程共用一个 Docker 守护进程、又在同一时刻跑同一个线程时，后到的一方会接管这个名字 ——
    /// 跨进程没有可以查证的活性记录，这一点写在文档的已知问题里。名字被别的什么容器占着时不动它，报错点名。
    /// </para>
    /// </remarks>
    private async Task RemoveOrphanAsync(HttpClient httpClient, string containerName, Guid threadId, CancellationToken ct)
    {
        using var inspect = await httpClient.GetAsync($"/containers/{containerName}/json", ct);
        if (inspect.StatusCode == System.Net.HttpStatusCode.NotFound)
            return; // 已经不在了（刚被删掉），直接重建

        if (!inspect.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to create Docker container: the name {containerName} is in use and its container could not be inspected (HTTP {(int)inspect.StatusCode}).");
        }

        var (existingId, labels) = await ReadIdAndLabelsAsync(inspect, ct);
        var isThisThreadsSandbox = labels.TryGetValue("tnzi.sandbox", out var marker) && marker == "true"
            && labels.TryGetValue("tnzi.sandbox.thread", out var thread) && thread == threadId.ToString("N");
        if (!isThisThreadsSandbox || string.IsNullOrEmpty(existingId))
        {
            throw new InvalidOperationException(
                $"Failed to create Docker container: the name {containerName} is in use by a container that is not this thread's sandbox. Remove or rename it.");
        }

        _logger.LogWarning(
            "Docker sandbox for thread {ThreadId}: removing container {ContainerId} left running under {ContainerName} by a run that never released it",
            threadId, existingId, containerName);

        using var remove = await httpClient.DeleteAsync($"/containers/{existingId}?force=true", ct);
        if (!remove.IsSuccessStatusCode && remove.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Failed to create Docker container: the name {containerName} is held by a leftover sandbox container that could not be removed (HTTP {(int)remove.StatusCode}).");
        }
    }

    private static async Task<(string? Id, IReadOnlyDictionary<string, string> Labels)> ReadIdAndLabelsAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            var id = root.TryGetProperty("Id", out var idElement) ? idElement.GetString() : null;

            if (root.TryGetProperty("Config", out var config)
                && config.ValueKind == JsonValueKind.Object
                && config.TryGetProperty("Labels", out var labelsElement)
                && labelsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var label in labelsElement.EnumerateObject())
                {
                    if (label.Value.ValueKind == JsonValueKind.String)
                        labels[label.Name] = label.Value.GetString()!;
                }
            }

            return (id, labels);
        }
        catch (JsonException)
        {
            return (null, labels);
        }
    }

    /// <summary>
    /// 尽力删除一个已创建但未能投入使用的容器。
    /// </summary>
    /// <remarks>
    /// 用 <c>force=true</c>：容器可能处于 created / 半启动状态，普通 remove 会被拒。
    /// 刻意<b>不接</b>调用方的取消令牌：走到这里往往正是因为它已经取消了，拿它去发删除请求
    /// 等于不删。
    /// </remarks>
    private async Task TryRemoveContainerAsync(HttpClient httpClient, string containerId, string sandboxId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/containers/{containerId}?force=true", CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Docker sandbox {SandboxId}: failed to remove container {ContainerId} after a failed start: HTTP {StatusCode}. " +
                    "The name is derived from the thread id; the next sandbox of this thread removes it before creating its own.",
                    sandboxId, containerId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Docker sandbox {SandboxId}: error removing container {ContainerId} after a failed start.",
                sandboxId, containerId);
        }
    }

    /// <summary>
    /// Backwards-compatible disposal entry point. The real cleanup lives in
    /// <see cref="DockerSandbox.DisposeAsync"/>; this method forwards to it so
    /// callers using <c>provider.DisposeAsync(sandbox)</c> still work, while
    /// <c>await using var sandbox</c> (the canonical path) also releases the slot.
    /// </summary>
    public async Task DisposeAsync(ISandbox sandbox, CancellationToken ct = default)
    {
        Check.NotNull(sandbox);
        await sandbox.DisposeAsync();
        _logger.LogInformation("Docker sandbox {SandboxId} disposed", sandbox.Id);
    }

    /// <summary>
    /// 创建容器并返回其 id；名字已被占用（HTTP 409）时返回 null，由调用方决定怎么处理。
    /// </summary>
    private async Task<string?> CreateContainerAsync(HttpClient httpClient, string containerName,
        SandboxCreateOptions options, DockerSandboxOptions dockerOpts, CancellationToken ct)
    {
        // CPU 限制: Docker 使用 NanoCPUs (1e9 = 1 CPU)
        var nanoCpus = (long)(dockerOpts.CpuLimit * 1_000_000_000);
        // 内存限制: Docker 使用字节
        var memoryBytes = (long)dockerOpts.MemoryLimitMb * 1024 * 1024;

        // Docker API expects Env as ["KEY=VALUE", ...] at the root of the create body.
        var envList = options.EnvironmentOverrides is { Count: > 0 }
            ? options.EnvironmentOverrides.Select(kv => $"{kv.Key}={kv.Value}").ToArray()
            : null;

        var securityOpts = BuildSecurityOpts(dockerOpts.ExtraSecurityOpts);
        var capDrop = dockerOpts.DropAllCapabilities ? new[] { "ALL" } : null;
        // PidsLimit <= 0 disables the limit (Docker treats null/absent as unlimited).
        long? pidsLimit = dockerOpts.PidsLimit > 0 ? dockerOpts.PidsLimit : null;
        // When the rootfs is read-only, Python libs (PIL fontconfig cache, weasyprint
        // tempfiles) still need a writable scratch space - give them /tmp via tmpfs.
        var tmpfs = dockerOpts.ReadonlyRootfs
            ? new Dictionary<string, string> { ["/tmp"] = "size=64m,nosuid,nodev" }
            : null;

        var createBody = new
        {
            Image = dockerOpts.Image,
            Cmd = new[] { "tail", "-f", "/dev/null" }, // 保持容器运行
            WorkingDir = DockerBindSources.ThreadMountPoint,
            // Override the image's USER directive explicitly - defends against
            // accidentally pulling a tampered image with USER root.
            User = dockerOpts.RunAsUser,
            Env = envList,
            HostConfig = new
            {
                // 挂载源必须是绝对路径（相对路径会被 Engine 当成卷名或直接拒绝）：
                // VirtualPathTranslator 在构造时就把 DataRoot 归一化成了绝对路径，线程目录随之绝对。
                // 线程目录挂在 /workspace，四个子目录另挂在 /mnt/*，与 agent 看到的虚拟路径一致。
                Binds = DockerBindSources.BuildBinds(options.WorkspacePath),
                NanoCPUs = nanoCpus,
                Memory = memoryBytes,
                AutoRemove = dockerOpts.AutoRemove,
                NetworkMode = "none", // 安全隔离: 无网络访问
                ReadonlyRootfs = dockerOpts.ReadonlyRootfs,
                PidsLimit = pidsLimit,
                CapDrop = capDrop,
                SecurityOpt = securityOpts,
                Tmpfs = tmpfs
            },
            Labels = new Dictionary<string, string>
            {
                ["tnzi.sandbox"] = "true",
                ["tnzi.sandbox.thread"] = options.ThreadId.ToString("N")
            }
        };

        var response = await httpClient.PostAsJsonAsync(
            $"/containers/create?name={containerName}", createBody, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);

            // Docker returns 404 with `{"message":"No such image: <name>"}` when the
            // configured image is not present locally. Translate that into actionable
            // guidance instead of leaking the raw API payload.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                && error.Contains("No such image", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Docker image '{dockerOpts.Image}' is not present on this host. " +
                    $"Either pull it (`docker pull {dockerOpts.Image}`) or build it from " +
                    $"the repository: `docker build -t {dockerOpts.Image} -f docker/sandbox/Dockerfile docker/sandbox`. " +
                    "See docs/modules/ai-sandbox.md for setup instructions.");
            }

            throw new InvalidOperationException($"Failed to create Docker container: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<DockerCreateResponse>(ct);
        if (result is null || string.IsNullOrEmpty(result.Id))
            throw new InvalidOperationException("Failed to parse container creation response");

        if (result.Warnings is { Count: > 0 })
        {
            foreach (var warning in result.Warnings)
                _logger.LogWarning("Docker container creation warning: {Warning}", warning);
        }

        return result.Id;
    }

    private static string[] BuildSecurityOpts(List<string> extras)
    {
        // "no-new-privileges" is always applied; additional opts come from config.
        if (extras is not { Count: > 0 })
            return ["no-new-privileges"];

        var list = new List<string>(extras.Count + 1) { "no-new-privileges" };
        list.AddRange(extras);
        return [.. list];
    }

    private static async Task StartContainerAsync(HttpClient httpClient, string containerId, CancellationToken ct)
    {
        var response = await httpClient.PostAsync($"/containers/{containerId}/start", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to start container: {error}");
        }
    }

    // Docker API response models
    private sealed class DockerCreateResponse
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Warnings")]
        public List<string>? Warnings { get; set; }
    }
}
