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
    private readonly ConcurrentDictionary<string, string> _activeContainers = new();
    private readonly SemaphoreSlim _containerSemaphore;

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
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            // 创建容器
            var containerId = await CreateContainerAsync(httpClient, containerName, options, dockerOpts, ct);

            // 启动容器
            await StartContainerAsync(httpClient, containerId, ct);

            _activeContainers[sandboxId] = containerId;

            _logger.LogInformation("Docker sandbox {SandboxId} created (container: {ContainerId}, image: {Image})",
                sandboxId, containerId[..12], dockerOpts.Image);

            return new DockerSandbox(
                id: sandboxId,
                httpClient: httpClient,
                containerId: containerId,
                workspacePath: "/workspace",
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
                maxFileSize: options.MaxFileSizeBytes);
        }
        catch
        {
            _containerSemaphore.Release();
            throw;
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

    private async Task<string> CreateContainerAsync(HttpClient httpClient, string containerName,
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
            WorkingDir = "/workspace",
            // Override the image's USER directive explicitly - defends against
            // accidentally pulling a tampered image with USER root.
            User = dockerOpts.RunAsUser,
            Env = envList,
            HostConfig = new
            {
                Binds = new[] { $"{options.WorkspacePath}:/workspace" },
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
