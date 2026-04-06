using System.Collections.Concurrent;

namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Docker container sandbox provider — creates and manages Docker containers
/// via the Docker Engine REST API (no Docker.DotNet dependency).
/// Supports both Unix socket (Linux/macOS) and named pipe (Windows) connections.
/// </summary>
public class DockerSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DockerSandboxProvider> _logger;
    private readonly ConcurrentDictionary<string, string> _activeContainers = new();
    private readonly SemaphoreSlim _containerSemaphore;

    public const string HttpClientName = "DockerEngine";

    public string Name => "docker";

    public DockerSandboxProvider(IOptions<SandboxModuleOptions> options,
        IHttpClientFactory httpClientFactory, ILogger<DockerSandboxProvider> logger)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
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
                logger: _logger);
        }
        catch
        {
            _containerSemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Stops and removes a Docker container sandbox, releasing resources.
    /// Always attempts cleanup even if errors occur.
    /// </summary>
    public async Task DisposeAsync(ISandbox sandbox, CancellationToken ct = default)
    {
        Check.NotNull(sandbox);

        if (!_activeContainers.TryRemove(sandbox.Id, out var containerId))
        {
            _logger.LogWarning("Container for sandbox {SandboxId} not found in active list", sandbox.Id);
            return;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            // 停止容器 (5 秒超时)
            await StopContainerAsync(httpClient, containerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop container {ContainerId} for sandbox {SandboxId}",
                containerId[..Math.Min(12, containerId.Length)], sandbox.Id);
        }

        try
        {
            // 删除容器 (force 确保即使停止失败也能清理)
            await RemoveContainerAsync(httpClient, containerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove container {ContainerId} for sandbox {SandboxId}",
                containerId[..Math.Min(12, containerId.Length)], sandbox.Id);
        }
        finally
        {
            _containerSemaphore.Release();
            _logger.LogInformation("Docker sandbox {SandboxId} disposed", sandbox.Id);
        }
    }

    private async Task<string> CreateContainerAsync(HttpClient httpClient, string containerName,
        SandboxCreateOptions options, DockerSandboxOptions dockerOpts, CancellationToken ct)
    {
        // CPU 限制: Docker 使用 NanoCPUs (1e9 = 1 CPU)
        var nanoCpus = (long)(dockerOpts.CpuLimit * 1_000_000_000);
        // 内存限制: Docker 使用字节
        var memoryBytes = (long)dockerOpts.MemoryLimitMb * 1024 * 1024;

        var createBody = new
        {
            Image = dockerOpts.Image,
            Cmd = new[] { "tail", "-f", "/dev/null" }, // 保持容器运行
            WorkingDir = "/workspace",
            HostConfig = new
            {
                Binds = new[] { $"{options.WorkspacePath}:/workspace" },
                NanoCPUs = nanoCpus,
                Memory = memoryBytes,
                AutoRemove = dockerOpts.AutoRemove,
                NetworkMode = "none", // 安全隔离: 无网络访问
                ReadonlyRootfs = false,
                SecurityOpt = new[] { "no-new-privileges" }
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

    private static async Task StartContainerAsync(HttpClient httpClient, string containerId, CancellationToken ct)
    {
        var response = await httpClient.PostAsync($"/containers/{containerId}/start", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to start container: {error}");
        }
    }

    private static async Task StopContainerAsync(HttpClient httpClient, string containerId, CancellationToken ct)
    {
        var response = await httpClient.PostAsync($"/containers/{containerId}/stop?t=5", null, ct);
        // 304 = container already stopped, which is fine
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 304)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to stop container: {error}");
        }
    }

    private static async Task RemoveContainerAsync(HttpClient httpClient, string containerId, CancellationToken ct)
    {
        var response = await httpClient.DeleteAsync($"/containers/{containerId}?force=true&v=true", ct);
        // 404 = container already removed (e.g., AutoRemove was on), which is fine
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to remove container: {error}");
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
