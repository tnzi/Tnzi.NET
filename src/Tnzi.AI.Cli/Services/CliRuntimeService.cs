namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 外部运行时注册表的真实实现。
/// </summary>
public class CliRuntimeService : ApplicationService, ICliRuntimeService
{
    private readonly IRepository<Entities.CliRuntime, Guid> _repository;
    private readonly IRepository<CliAgentBinding, Guid> _bindingRepository;
    private readonly ICliProviderRegistry _providerRegistry;
    private readonly ICliProtocolAdapterFactory _adapterFactory;
    private readonly ICliExecutableResolver _executableResolver;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly string _hostId = Environment.MachineName;

    /// <summary>初始化运行时注册表服务。</summary>
    public CliRuntimeService(
        IRepository<Entities.CliRuntime, Guid> repository,
        IRepository<CliAgentBinding, Guid> bindingRepository,
        ICliProviderRegistry providerRegistry,
        ICliProtocolAdapterFactory adapterFactory,
        ICliExecutableResolver executableResolver,
        IOptionsMonitor<CliAgentOptions> options,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _bindingRepository = Check.NotNull(bindingRepository);
        _providerRegistry = Check.NotNull(providerRegistry);
        _adapterFactory = Check.NotNull(adapterFactory);
        _executableResolver = Check.NotNull(executableResolver);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task<Result<List<CliRuntimeDto>>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var runtimes = await _repository.AsQueryable()
            .OrderBy(r => r.ProviderKey)
            .ToListAsync(cancellationToken);

        return Ok(runtimes.Select(Project).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<CliRuntimeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var runtime = await _repository.GetAsync(id, cancellationToken);
        return runtime is null
            ? Fail<CliRuntimeDto>("External CLI runtime not found.", 404, ErrorCodes.CliRuntimeNotFound)
            : Ok(Project(runtime));
    }

    /// <inheritdoc />
    public async Task<Result<CliRuntimeProbeResultDto>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return Fail<CliRuntimeProbeResultDto>(
                "External CLI agent execution is disabled (AI:Cli:Enabled=false).", 501, ErrorCodes.CliDisabled);
        }

        var result = new CliRuntimeProbeResultDto();
        var now = DateTime.UtcNow;

        foreach (var provider in _providerRegistry.GetEnabled())
        {
            var executablePath = _executableResolver.Resolve(provider);
            if (executablePath is null)
            {
                result.NotFound.Add(provider.Key);
                await MarkOfflineAsync(provider.Key, cancellationToken);
                continue;
            }

            var version = await _executableResolver.DetectVersionAsync(executablePath, cancellationToken);
            var runtime = await _repository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(r => r.HostId == _hostId && r.ProviderKey == provider.Key, cancellationToken);

            if (runtime is null)
            {
                runtime = new Entities.CliRuntime
                {
                    HostId = _hostId,
                    ProviderKey = provider.Key,
                    Name = $"{provider.DisplayName} @ {_hostId}",
                    ExecutablePath = executablePath,
                    CliVersion = version,
                    Mode = CliRuntimeMode.InProcess,
                    Status = CliRuntimeStatus.Online,
                    LastSeenAt = now,
                    HostInfoJson = BuildHostInfo(),
                    MaxConcurrentRuns = _options.CurrentValue.MaxConcurrentRuns
                };

                await _repository.InsertAsync(runtime, cancellationToken);
            }
            else
            {
                runtime.ExecutablePath = executablePath;
                runtime.CliVersion = version;
                runtime.LastSeenAt = now;
                runtime.HostInfoJson = BuildHostInfo();

                // 管理员手工停用的运行时不因为「探测到它还在」就自动复活 ——
                // 停用是一个决定，不是一次观测结果。
                if (runtime.Status != CliRuntimeStatus.Disabled)
                {
                    runtime.Status = CliRuntimeStatus.Online;
                }

                await _repository.UpdateAsync(runtime, cancellationToken);
            }

            await _repository.SaveChangesAsync(cancellationToken);
            result.Runtimes.Add(Project(runtime));
        }

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<CliRuntimeDto>> UpdateAsync(
        Guid id, UpdateCliRuntimeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var runtime = await _repository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (runtime is null)
        {
            return Fail<CliRuntimeDto>("External CLI runtime not found.", 404, ErrorCodes.CliRuntimeNotFound);
        }

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            runtime.Name = input.Name;
        }

        if (input.Status is { } status)
        {
            // Offline 是探测结果，不是管理员能设的值：手工写成 Offline 会在下一轮探测时
            // 被改回 Online，看起来像"设置没生效"。要停用就用 Disabled。
            if (status == CliRuntimeStatus.Offline)
            {
                return Fail<CliRuntimeDto>(
                    "Offline is a probe outcome, not a manual state. Use Disabled to take a runtime out of service.",
                    400, ErrorCodes.InternalError);
            }

            runtime.Status = status;
        }

        if (input.MaxConcurrentRuns is { } max && max > 0)
        {
            runtime.MaxConcurrentRuns = max;
        }

        await _repository.UpdateAsync(runtime, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Ok(Project(runtime));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var runtime = await _repository.GetAsync(id, cancellationToken);
        if (runtime is null)
        {
            return Fail("External CLI runtime not found.", 404, ErrorCodes.CliRuntimeNotFound);
        }

        // 有 Agent 绑在上面就不能删：删掉会让那些 Agent 在下一次运行时才发现自己
        // 指向了一个不存在的运行时，而那时报的错离真正的原因已经很远了。
        var boundAgents = await _bindingRepository.CountAsync(b => b.CliRuntimeId == id, cancellationToken);
        if (boundAgents > 0)
        {
            return Fail(
                $"{boundAgents} agent(s) are still bound to this runtime. Unbind them first.",
                409, ErrorCodes.CliRunInvalidState);
        }

        await _repository.DeleteAsync(runtime, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public Task<Result<List<CliProviderOptionDto>>> GetProviderOptionsAsync(CancellationToken cancellationToken = default)
    {
        var options = _providerRegistry.GetAll()
            .Select(p => new CliProviderOptionDto
            {
                Key = p.Key,
                DisplayName = p.DisplayName,
                Protocol = p.Protocol.ToString(),
                DefaultExecutable = p.DefaultExecutable,
                LaunchHeader = p.LaunchHeader,
                Enabled = p.Enabled,
                // 描述表里存在不等于可用。诚实地把这一位报出去，好过让管理员选中它
                // 之后在第一次运行时收到 501。
                Implemented = _adapterFactory.IsImplemented(p.Protocol)
            })
            .OrderBy(p => p.DisplayName)
            .ToList();

        return Task.FromResult(Ok(options));
    }

    private async Task MarkOfflineAsync(string providerKey, CancellationToken cancellationToken)
    {
        await _repository.AsQueryable()
            .Where(r => r.HostId == _hostId
                        && r.ProviderKey == providerKey
                        && r.Status == CliRuntimeStatus.Online)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRuntimeStatus.Offline), cancellationToken);
    }

    private CliRuntimeDto Project(Entities.CliRuntime runtime)
    {
        var dto = runtime.MapTo<CliRuntimeDto>();
        var provider = _providerRegistry.Find(runtime.ProviderKey);
        dto.ProviderDisplayName = provider?.DisplayName;
        dto.Protocol = provider?.Protocol.ToString();
        dto.LaunchHeader = provider?.LaunchHeader;
        return dto;
    }

    private static string BuildHostInfo() => JsonSerializer.Serialize(new
    {
        machineName = Environment.MachineName,
        osDescription = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.OSArchitecture.ToString(),
        processorCount = Environment.ProcessorCount
    }, TnziJsonDefaults.Options);
}
