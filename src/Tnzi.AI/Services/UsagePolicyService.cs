namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IUsagePolicyService"/> 的默认实现：策略存在 <see cref="AiUsagePolicy"/>，最底层取 <see cref="UsageLimitOptions"/>，
/// 模型清单取 <see cref="AiChatOptions.Models"/>。
/// </summary>
public class UsagePolicyService : ApplicationService, IUsagePolicyService
{
    private readonly IRepository<AiUsagePolicy, Guid> _repository;
    private readonly IOptionsMonitor<UsageLimitOptions> _limitOptions;
    private readonly IOptionsMonitor<AiChatOptions> _chatOptions;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IRepository<Agent, Guid>? _agentRepository;
    private readonly ICliAgentBindingService? _bindingService;
    private readonly ICliRuntimeService? _runtimeService;
    private readonly IChatModelTargetResolver? _targetResolver;
    private readonly IAgentThreadService? _threads;
    private readonly ICurrentTenant? _currentTenant;

    public UsagePolicyService(
        IRepository<AiUsagePolicy, Guid> repository,
        IOptionsMonitor<UsageLimitOptions> limitOptions,
        IOptionsMonitor<AiChatOptions> chatOptions,
        IOptionsMonitor<AIOptions> aiOptions,
        IServiceProvider serviceProvider,
        IRepository<Agent, Guid>? agentRepository = null,
        ICliAgentBindingService? bindingService = null,
        ICurrentTenant? currentTenant = null,
        ICliRuntimeService? runtimeService = null,
        IChatModelTargetResolver? targetResolver = null,
        IAgentThreadService? threads = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _limitOptions = Check.NotNull(limitOptions);
        _chatOptions = Check.NotNull(chatOptions);
        _aiOptions = Check.NotNull(aiOptions);
        _agentRepository = agentRepository;
        _bindingService = bindingService;
        _currentTenant = currentTenant;
        _runtimeService = runtimeService;
        _targetResolver = targetResolver;
        _threads = threads;
    }

    /// <inheritdoc />
    public async Task<Result<UsageLimitDefaultsDto>> GetDefaultsAsync(CancellationToken ct = default)
    {
        var defaults = await FindDefaultsAsync(ct);
        return Ok(BuildDefaults(defaults));
    }

    /// <inheritdoc />
    public async Task<Result<UsageLimitDefaultsDto>> SaveDefaultsAsync(SaveUsagePolicyDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        // 默认值的「不设」跟随配置文件，所以按配置文件那一层校验默认模型
        var invalid = Validate(input, ConfigurationLayer());
        if (invalid is not null)
        {
            return Fail<UsageLimitDefaultsDto>(invalid, 400, ErrorCodes.UsagePolicyInvalid);
        }

        var row = await FindDefaultsAsync(ct, tracking: true);
        if (row is null)
        {
            row = new AiUsagePolicy { Id = DefaultPolicyId(CurrentTenantId) };
            Apply(row, input);
            if (!await TryInsertAsync(row, ct))
            {
                // 另一个管理员同时建了默认行（确定的主键撞上了）：改成更新它
                row = await FindDefaultsAsync(ct, tracking: true)
                      ?? throw new InvalidOperationException("The default usage policy row vanished after a concurrent insert.");
                Apply(row, input);
                await _repository.UpdateAsync(row, ct);
            }
        }
        else
        {
            Apply(row, input);
            await _repository.UpdateAsync(row, ct);
        }

        await _repository.SaveChangesAsync(ct);
        Logger.LogInformation("Saved the default AI usage policy");
        return Ok(BuildDefaults(row));
    }

    /// <inheritdoc />
    public async Task<Result<UserUsageLimitsDto>> GetUserPolicyAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await FindUserPolicyAsync(userId, ct);
        var defaults = await FindDefaultsAsync(ct);
        return Ok(BuildUserPolicy(userId, row, defaults));
    }

    /// <inheritdoc />
    public async Task<Result<UserUsageLimitsDto>> SaveUserPolicyAsync(Guid userId, SaveUsagePolicyDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var defaults = await FindDefaultsAsync(ct);
        var invalid = Validate(input, Merge(defaults, ConfigurationLayer()));
        if (invalid is not null)
        {
            return Fail<UserUsageLimitsDto>(invalid, 400, ErrorCodes.UsagePolicyInvalid);
        }

        var row = await FindUserPolicyAsync(userId, ct, tracking: true);
        if (row is null)
        {
            row = new AiUsagePolicy { UserId = userId };
            Apply(row, input);
            if (!await TryInsertAsync(row, ct))
            {
                row = await FindUserPolicyAsync(userId, ct, tracking: true)
                      ?? throw new InvalidOperationException($"The usage policy of user {userId} vanished after a concurrent insert.");
                Apply(row, input);
                await _repository.UpdateAsync(row, ct);
            }
        }
        else
        {
            Apply(row, input);
            await _repository.UpdateAsync(row, ct);
        }

        await _repository.SaveChangesAsync(ct);
        Logger.LogInformation("Saved the AI usage policy of user {UserId}", userId);
        return Ok(BuildUserPolicy(userId, row, defaults));
    }

    /// <inheritdoc />
    public async Task<Result> ClearUserPolicyAsync(Guid userId, CancellationToken ct = default)
    {
        await _repository.EnsureTransactionStartedAsync(ct);
        var deleted = await _repository.AsQueryable()
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            Logger.LogInformation("Cleared the AI usage policy of user {UserId}", userId);
        }

        return Ok();
    }

    /// <inheritdoc />
    public async Task<EffectiveUsagePolicyDto> ResolveAsync(Guid? userId, CancellationToken ct = default)
    {
        var defaults = await FindDefaultsAsync(ct);
        var user = userId is { } id ? await FindUserPolicyAsync(id, ct) : null;
        return Effective(Merge(user, Merge(defaults, ConfigurationLayer())));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, (bool HasCustomPolicy, decimal? SessionLimitUsd, decimal? WeeklyLimitUsd)>> ResolveLimitsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        Check.NotNull(userIds);

        var defaults = Merge(await FindDefaultsAsync(ct), ConfigurationLayer());
        var ids = userIds.Distinct().ToList();
        var rows = ids.Count == 0
            ? []
            : await _repository.AsQueryable()
                .Where(p => p.UserId != null && ids.Contains(p.UserId.Value))
                .ToListAsync(ct);
        var byUser = rows.ToDictionary(p => p.UserId!.Value);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var layer = Merge(byUser.GetValueOrDefault(id), defaults);
                return (byUser.ContainsKey(id), layer.SessionLimitUsd, layer.WeeklyLimitUsd);
            });
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatModelDto> GetCatalog()
        => (_chatOptions.CurrentValue.Models ?? [])
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new ChatModelDto
            {
                Id = m.Id.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id.Trim() : m.DisplayName.Trim(),
                Provider = string.IsNullOrWhiteSpace(m.Provider) ? null : m.Provider.Trim(),
                Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim(),
                Efforts = ChatModelEfforts.Normalize(m.Efforts),
                DefaultEffort = ChatModelEfforts.Find(m.Efforts, m.DefaultEffort),
                EffortsDeclared = m.Efforts is { Count: > 0 }
            })
            .ToList();

    /// <inheritdoc />
    public Task<Result<ChatModelsDto>> GetChatModelsAsync(Guid? userId, Guid? agentId, string? provider = null, CancellationToken ct = default)
        => GetChatModelsAsync(new ChatModelsRequest(userId, agentId, provider), ct);

    /// <inheritdoc />
    public async Task<Result<ChatModelsDto>> GetChatModelsAsync(ChatModelsRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);
        if (GetCatalog().Count == 0)
        {
            return Ok(new ChatModelsDto());
        }

        var effective = await ResolveAsync(request.UserId, ct);
        var (agentProvider, binding) = await ResolveAgentProviderAsync(request.AgentId, request.Provider, ct);
        var target = await ResolveTargetAsync(request, binding, ct);

        // 对话实际落在别家 CLI 上时按那家列：模型属于那家，握手档位取那家的运行时，绑定上按 claude 写的 ThinkingLevel 不作默认档
        var provider = target?.ProviderKey ?? agentProvider;
        var sameProviderAsBinding = binding is not null && string.Equals(provider, binding.ProviderKey, StringComparison.OrdinalIgnoreCase);
        var runtimeId = target?.RuntimeId ?? (sameProviderAsBinding ? binding!.CliRuntimeId : null);
        var bindingEffort = sameProviderAsBinding ? binding!.ThinkingLevel : null;

        var models = effective.AllowedModels.Where(m => Belongs(m, provider)).ToList();
        if (target?.Model is { } fixedModel)
        {
            // 这段对话固定用这个模型：只列它（清单里有就用清单那一项，带它的档位；没有就按使用方给的名字列出）
            var listed = effective.AllowedModels.FirstOrDefault(m => string.Equals(m.Id, fixedModel, StringComparison.OrdinalIgnoreCase))
                         ?? GetCatalog().FirstOrDefault(m => string.Equals(m.Id, fixedModel, StringComparison.OrdinalIgnoreCase));
            models =
            [
                listed ?? new ChatModelDto
                {
                    Id = fixedModel,
                    DisplayName = string.IsNullOrWhiteSpace(target.ModelDisplayName) ? fixedModel : target.ModelDisplayName.Trim(),
                    Provider = provider
                }
            ];
        }

        if (runtimeId is { } runtime)
        {
            models = await WithRuntimeEffortsAsync(models, runtime, bindingEffort, ct);
        }

        return Ok(new ChatModelsDto
        {
            Models = models,
            DefaultModel = target?.Model is not null ? models[0].Id : PickDefault(models, effective.DefaultModel)
        });
    }

    /// <summary>
    /// 使用方说的这段对话的落点（<see cref="IChatModelTargetResolver"/>）；没注册、不带 Agent、或说了「按绑定」时为 null。
    /// 不是这个用户的线程按没给线程处理。
    /// </summary>
    private async Task<ChatModelTarget?> ResolveTargetAsync(ChatModelsRequest request, CliAgentBindingDto? binding, CancellationToken ct)
    {
        if (_targetResolver is null || request.AgentId is not { } agentId || agentId == Guid.Empty)
        {
            return null;
        }

        var threadId = request.ThreadId;
        if (threadId is { } thread && (request.UserId is not { } user || _threads is null || !await _threads.IsOwnerAsync(thread, user)))
        {
            threadId = null;
        }

        var targetRequest = new ChatModelTargetRequest
        {
            UserId = request.UserId,
            AgentId = agentId,
            ThreadId = threadId,
            BindingProviderKey = binding?.ProviderKey
        };
        if (request.Metadata is { } metadata)
        {
            targetRequest = targetRequest with { Metadata = metadata };
        }

        var target = await _targetResolver.ResolveAsync(targetRequest, ct);
        if (target is null)
        {
            return null;
        }

        var normalized = target with
        {
            ProviderKey = string.IsNullOrWhiteSpace(target.ProviderKey) ? null : target.ProviderKey.Trim(),
            Model = string.IsNullOrWhiteSpace(target.Model) ? null : target.Model.Trim()
        };
        return normalized is { ProviderKey: null, Model: null, RuntimeId: null } ? null : normalized;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> GetModelEffortsAsync(
        Guid? userId, string providerKey, string? modelId, Guid? runtimeId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(providerKey);
        var effective = await ResolveAsync(userId, ct);
        var model = string.IsNullOrWhiteSpace(modelId)
            ? null
            : effective.AllowedModels.FirstOrDefault(m => string.Equals(m.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase)
                                                         && Belongs(m, providerKey));
        if (model is { EffortsDeclared: true })
        {
            return model.Efforts;
        }

        if (runtimeId is null || _runtimeService is null)
        {
            return [];
        }

        var runtime = await _runtimeService.GetAsync(runtimeId.Value, ct);
        return ChatModelEfforts.Normalize(runtime.Succeeded ? runtime.Data?.Capabilities?.ReasoningEfforts : null);
    }

    /// <inheritdoc />
    public async Task<Result<string?>> ResolveChatModelAsync(
        Guid? userId, Guid? agentId, string? provider, string? requestedModel, CancellationToken ct = default)
    {
        var turn = await ResolveChatTurnAsync(userId, agentId, provider, requestedModel, null, ct);
        return turn.Succeeded
            ? Ok<string?>(turn.Data!.Model)
            : Fail<string?>(turn.Message!, turn.Code ?? 403, turn.ErrorCode ?? ErrorCodes.ModelNotAllowed);
    }

    /// <inheritdoc />
    public Task<Result<ChatTurnModel>> ResolveChatTurnAsync(
        Guid? userId, Guid? agentId, string? provider, string? requestedModel, string? requestedEffort, CancellationToken ct = default)
        => ResolveChatTurnAsync(new ChatModelsRequest(userId, agentId, provider), requestedModel, requestedEffort, ct);

    /// <inheritdoc />
    public async Task<Result<ChatTurnModel>> ResolveChatTurnAsync(
        ChatModelsRequest request, string? requestedModel, string? requestedEffort, CancellationToken ct = default)
    {
        Check.NotNull(request);
        var (userId, agentId) = (request.UserId, request.AgentId);
        var effort = string.IsNullOrWhiteSpace(requestedEffort) ? null : requestedEffort.Trim();
        var catalog = GetCatalog();
        if (catalog.Count == 0)
        {
            return effort is null
                ? Ok(new ChatTurnModel(requestedModel, null))
                : RefuseEffort(effort, null, userId, agentId);
        }

        var available = await GetChatModelsAsync(request, ct);
        var models = available.Data!.Models;

        ChatModelDto? model;
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            model = models.FirstOrDefault(m => string.Equals(m.Id, available.Data.DefaultModel, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var requested = requestedModel.Trim();
            model = models.FirstOrDefault(m => string.Equals(m.Id, requested, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                Logger.LogInformation(
                    "Refused model {Model} for user {UserId} on agent {AgentId}: not among the {Count} models this user may use there",
                    requested, userId, agentId, models.Count);
                return Fail<ChatTurnModel>(
                    $"The model '{requested}' is not available to you for this assistant.", 403, ErrorCodes.ModelNotAllowed);
            }
        }

        if (effort is null)
        {
            return Ok(new ChatTurnModel(model?.Id, null));
        }

        // 强度属于模型：按这一轮最终用的那个模型的档位校验（请求没带模型时是默认模型）
        var matched = ChatModelEfforts.Find(model?.Efforts, effort);
        return matched is not null
            ? Ok(new ChatTurnModel(model!.Id, matched))
            : RefuseEffort(effort, model, userId, agentId);
    }

    private Result<ChatTurnModel> RefuseEffort(string effort, ChatModelDto? model, Guid? userId, Guid? agentId)
    {
        Logger.LogInformation(
            "Refused reasoning effort {Effort} for user {UserId} on agent {AgentId}: model {Model} offers {Efforts}",
            effort, userId, agentId, model?.Id ?? "(none)", model is { Efforts.Count: > 0 } ? string.Join(", ", model.Efforts) : "no effort choice");
        var message = model is { Efforts.Count: > 0 }
            ? $"The reasoning effort '{effort}' is not available for the model '{model.Id}' (it offers {string.Join(", ", model.Efforts)})."
            : model is null
                ? $"The reasoning effort '{effort}' cannot be chosen: no model list is configured."
                : $"The model '{model.Id}' offers no choice of reasoning effort.";
        return Fail<ChatTurnModel>(message, 403, ErrorCodes.EffortNotAllowed);
    }

    #region 取值

    /// <summary>
    /// 一层策略的值：每一项 null = 这一层没设（跟随下一层）。限额那两项里「不限」用 <see cref="Unlimited"/> 表示。
    /// </summary>
    private sealed record Layer(
        LimitValue? Session,
        LimitValue? Weekly,
        IReadOnlyList<string>? AllowedModels,
        string? DefaultModel,
        IReadOnlyDictionary<string, ChatModelEffortsDto>? ModelEfforts = null)
    {
        public decimal? SessionLimitUsd => Session?.AmountUsd;

        public decimal? WeeklyLimitUsd => Weekly?.AmountUsd;
    }

    /// <summary>一层里设下的限额：金额，或者 null 金额表示不限。</summary>
    private sealed record LimitValue(decimal? AmountUsd);

    private static readonly LimitValue Unlimited = new((decimal?)null);

    /// <summary>配置文件那一层：每一项都有值（金额为 null 即不限；允许列表为空即清单里的全部）。</summary>
    private Layer ConfigurationLayer()
    {
        var options = _limitOptions.CurrentValue;
        var allowed = options.AllowedModels is { Count: > 0 }
            ? options.AllowedModels.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList()
            : GetCatalog().Select(m => m.Id).ToList();

        return new Layer(
            new LimitValue(options.SessionLimitUsd),
            new LimitValue(options.WeeklyLimitUsd),
            allowed,
            string.IsNullOrWhiteSpace(options.DefaultModel) ? null : options.DefaultModel.Trim());
    }

    /// <summary>上一层盖在下一层上：上一层没设的项取下一层的。</summary>
    private static Layer Merge(AiUsagePolicy? row, Layer lower)
        => Merge(row is null ? null : ToLayer(row), lower);

    private static Layer Merge(Layer? upper, Layer lower)
        => upper is null
            ? lower
            : new Layer(
                upper.Session ?? lower.Session,
                upper.Weekly ?? lower.Weekly,
                upper.AllowedModels ?? lower.AllowedModels,
                upper.DefaultModel ?? lower.DefaultModel,
                MergeEfforts(upper.ModelEfforts, lower.ModelEfforts));

    /// <summary>按模型逐个往下找：上一层列出的模型用上一层的，其余取下一层的。</summary>
    private static IReadOnlyDictionary<string, ChatModelEffortsDto>? MergeEfforts(
        IReadOnlyDictionary<string, ChatModelEffortsDto>? upper, IReadOnlyDictionary<string, ChatModelEffortsDto>? lower)
    {
        if (upper is not { Count: > 0 })
        {
            return lower;
        }

        if (lower is not { Count: > 0 })
        {
            return upper;
        }

        var merged = new Dictionary<string, ChatModelEffortsDto>(lower, StringComparer.OrdinalIgnoreCase);
        foreach (var (model, efforts) in upper)
        {
            merged[model] = efforts;
        }

        return merged;
    }

    private static Layer ToLayer(AiUsagePolicy row)
        => new(
            ToLimit(row.SessionLimitMode, row.SessionLimitUsd),
            ToLimit(row.WeeklyLimitMode, row.WeeklyLimitUsd),
            ReadModels(row.AllowedModelsJson),
            string.IsNullOrWhiteSpace(row.DefaultModel) ? null : row.DefaultModel,
            ReadModelEfforts(row.ModelEffortsJson));

    private static LimitValue? ToLimit(UsageLimitMode mode, decimal? amount) => mode switch
    {
        UsageLimitMode.Unlimited => Unlimited,
        UsageLimitMode.Limited when amount is { } value => new LimitValue(value),
        _ => null
    };

    /// <summary>生效值：允许列表与清单求交（按清单顺序，清单里没有的 id 丢掉），默认模型落在允许列表里。</summary>
    private EffectiveUsagePolicyDto Effective(Layer layer)
    {
        var allowed = layer.AllowedModels ?? [];
        var models = GetCatalog()
            .Where(m => allowed.Contains(m.Id, StringComparer.OrdinalIgnoreCase))
            .Select(m => WithPolicyEfforts(m, layer.ModelEfforts))
            .ToList();

        return new EffectiveUsagePolicyDto
        {
            SessionLimitUsd = layer.SessionLimitUsd,
            WeeklyLimitUsd = layer.WeeklyLimitUsd,
            AllowedModels = models,
            DefaultModel = PickDefault(models, layer.DefaultModel)
        };
    }

    /// <summary>策略里给这个模型设了强度时用策略的，否则留着模型清单那一层的。</summary>
    private static ChatModelDto WithPolicyEfforts(ChatModelDto model, IReadOnlyDictionary<string, ChatModelEffortsDto>? efforts)
    {
        if (efforts is null || !efforts.TryGetValue(model.Id, out var set))
        {
            return model;
        }

        var list = ChatModelEfforts.Normalize(set.Efforts);
        return Copy(model, list, ChatModelEfforts.Find(list, set.DefaultEffort), declared: true);
    }

    private static ChatModelDto Copy(ChatModelDto model, List<string> efforts, string? defaultEffort, bool declared)
        => new()
        {
            EffortsDeclared = declared,
            Id = model.Id,
            DisplayName = model.DisplayName,
            Provider = model.Provider,
            Description = model.Description,
            Efforts = efforts,
            DefaultEffort = defaultEffort
        };

    /// <summary>
    /// 策略与模型清单都没声明档位的模型，取这段对话实际运行时（绑定的，或使用方说的落点）握手时报的。
    /// 默认档是绑定上的推理强度（只在落点与绑定同一家、且它在报的档位里时），否则不预先选。
    /// </summary>
    /// <remarks>
    /// 策略里给了空列表的模型是明确不提供，不取握手报的。stream-json 运行时（Claude Code 等）握手不报档位，只能来自配置。
    /// 落点可能把一轮放到别的运行时上，这里按绑定的运行时说。
    /// </remarks>
    private async Task<List<ChatModelDto>> WithRuntimeEffortsAsync(
        List<ChatModelDto> models, Guid runtimeId, string? bindingEffort, CancellationToken ct)
    {
        if (_runtimeService is null || models.All(m => m.EffortsDeclared))
        {
            return models;
        }

        var runtime = await _runtimeService.GetAsync(runtimeId, ct);
        var reported = ChatModelEfforts.Normalize(runtime.Succeeded ? runtime.Data?.Capabilities?.ReasoningEfforts : null);
        if (reported.Count == 0)
        {
            return models;
        }

        var defaultEffort = ChatModelEfforts.Find(reported, bindingEffort);
        return models
            .Select(m => m.EffortsDeclared ? m : Copy(m, reported, defaultEffort, declared: false))
            .ToList();
    }

    private static string? PickDefault(IReadOnlyList<ChatModelDto> models, string? preferred)
        => models.FirstOrDefault(m => string.Equals(m.Id, preferred, StringComparison.OrdinalIgnoreCase))?.Id
           ?? models.FirstOrDefault()?.Id;

    /// <summary>模型属不属于这个 provider：清单项没写 provider 的哪个都能用；provider 判不出来时只有没写 provider 的能用。</summary>
    private static bool Belongs(ChatModelDto model, string? provider)
        => model.Provider is null
           || (provider is not null && string.Equals(model.Provider, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 这个 Agent 跑在哪个 provider 上：绑了外部 CLI 运行时的取运行时的 provider 键，否则取 Agent 自己的 provider（空 = 默认 provider）。
    /// 不带 Agent 的聊天取请求里的 provider（空 = 默认 provider）。
    /// </summary>
    private async Task<(string? Provider, CliAgentBindingDto? Binding)> ResolveAgentProviderAsync(
        Guid? agentId, string? requestProvider, CancellationToken ct)
    {
        var defaultProvider = _aiOptions.CurrentValue.DefaultProvider;

        if (agentId is not { } id || id == Guid.Empty)
        {
            return (string.IsNullOrWhiteSpace(requestProvider) ? defaultProvider : requestProvider, null);
        }

        if (_bindingService is not null && await _bindingService.GetByAgentIdAsync(id, ct) is { } binding)
        {
            return (binding.ProviderKey, binding);
        }

        if (_agentRepository is null)
        {
            return (null, null);
        }

        var agentProvider = await _agentRepository.AsQueryable()
            .Where(a => a.Id == id)
            .Select(a => a.Provider)
            .FirstOrDefaultAsync(ct);
        return (string.IsNullOrWhiteSpace(agentProvider) ? defaultProvider : agentProvider, null);
    }

    #endregion

    #region 存取

    private Guid? CurrentTenantId => _currentTenant?.Id;

    /// <summary>
    /// 一个租户的默认行的主键：由租户算出来，每次都一样。两个管理员同时第一次保存默认值时撞主键，后到的改成更新，
    /// 不会留下两行谁说了算都讲不清的默认值（<c>UserId IS NULL</c> 没法跨库用唯一索引约束）。
    /// </summary>
    internal static Guid DefaultPolicyId(Guid? tenantId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"ai-usage-policy:default:{tenantId?.ToString("N") ?? "host"}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private Task<AiUsagePolicy?> FindDefaultsAsync(CancellationToken ct, bool tracking = false)
        => _repository.AsQueryable(withTracking: tracking)
            .Where(p => p.UserId == null)
            .OrderBy(p => p.CreationTime)
            .FirstOrDefaultAsync(ct);

    private Task<AiUsagePolicy?> FindUserPolicyAsync(Guid userId, CancellationToken ct, bool tracking = false)
        => _repository.AsQueryable(withTracking: tracking)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

    /// <summary>插入一行；撞了唯一约束（并发首插）返回 false 并丢掉这个实体。</summary>
    private async Task<bool> TryInsertAsync(AiUsagePolicy row, CancellationToken ct)
    {
        try
        {
            await _repository.InsertAsync(row, ct);
            await _repository.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 插入失败的实体仍是 Added：不丢掉的话本作用域下一次 SaveChanges 会重放它
            _repository.Discard(row);
            return false;
        }
    }

    private static void Apply(AiUsagePolicy row, SaveUsagePolicyDto input)
    {
        row.SessionLimitMode = input.SessionLimit.Mode;
        row.SessionLimitUsd = input.SessionLimit.Mode == UsageLimitMode.Limited ? input.SessionLimit.AmountUsd : null;
        row.WeeklyLimitMode = input.WeeklyLimit.Mode;
        row.WeeklyLimitUsd = input.WeeklyLimit.Mode == UsageLimitMode.Limited ? input.WeeklyLimit.AmountUsd : null;
        row.AllowedModelsJson = input.AllowedModels is null
            ? null
            : JsonSerializer.Serialize(input.AllowedModels.Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        row.DefaultModel = string.IsNullOrWhiteSpace(input.DefaultModel) ? null : input.DefaultModel.Trim();
        row.ModelEffortsJson = input.ModelEfforts is not { Count: > 0 } efforts
            ? null
            : JsonSerializer.Serialize(efforts.ToDictionary(
                e => e.Key.Trim(),
                e =>
                {
                    var list = ChatModelEfforts.Normalize(e.Value.Efforts);
                    return new ChatModelEffortsDto { Efforts = list, DefaultEffort = ChatModelEfforts.Find(list, e.Value.DefaultEffort) };
                },
                StringComparer.OrdinalIgnoreCase), ModelEffortsJsonOptions);
        row.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
    }

    private static readonly JsonSerializerOptions ModelEffortsJsonOptions = new(JsonSerializerDefaults.Web);

    private static Dictionary<string, ChatModelEffortsDto>? ReadModelEfforts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ChatModelEffortsDto>>(json, ModelEffortsJsonOptions);
            return read is null ? null : new Dictionary<string, ChatModelEffortsDto>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string>? ReadModels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>校验一份要保存的策略；合规返回 null，否则返回写给管理员看的原因。</summary>
    /// <param name="input">要保存的策略。</param>
    /// <param name="lower">它「不设」的项会跟随的那一层（据此判断默认模型最终落不落在允许列表里）。</param>
    private string? Validate(SaveUsagePolicyDto input, Layer lower)
    {
        if (input.SessionLimit is null || input.WeeklyLimit is null)
        {
            return "Both the 5-hour limit and the weekly limit must be given (use Inherit to follow the default).";
        }

        foreach (var (name, setting) in new[] { ("5-hour", input.SessionLimit), ("weekly", input.WeeklyLimit) })
        {
            if (!Enum.IsDefined(setting.Mode))
            {
                return $"The {name} limit has an unknown mode.";
            }

            if (setting.Mode == UsageLimitMode.Limited && setting.AmountUsd is not >= 0)
            {
                return $"The {name} limit is set to a fixed amount, so it needs an amount of 0 or more.";
            }
        }

        var catalog = GetCatalog();
        if (input.AllowedModels is not null)
        {
            if (input.AllowedModels.Count == 0)
            {
                return "The allowed models cannot be an empty list. Leave it unset to follow the default.";
            }

            var unknown = input.AllowedModels
                .Where(m => string.IsNullOrWhiteSpace(m) || !catalog.Any(c => string.Equals(c.Id, m.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unknown.Count > 0)
            {
                return $"These models are not in the model list (AI:Chat:Models): {string.Join(", ", unknown)}.";
            }
        }

        foreach (var (modelId, efforts) in input.ModelEfforts ?? [])
        {
            if (string.IsNullOrWhiteSpace(modelId) || !catalog.Any(c => string.Equals(c.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return $"Reasoning efforts are set for '{modelId}', which is not in the model list (AI:Chat:Models).";
            }

            if (efforts is null)
            {
                return $"The reasoning efforts of '{modelId}' are missing. Leave the model out to follow the default.";
            }

            if (ChatModelEfforts.Validate(efforts.Efforts, efforts.DefaultEffort) is { } invalid)
            {
                return $"The reasoning efforts of '{modelId}': {invalid}";
            }
        }

        if (!string.IsNullOrWhiteSpace(input.DefaultModel))
        {
            var allowed = input.AllowedModels ?? lower.AllowedModels ?? [];
            if (!allowed.Any(m => string.Equals(m.Trim(), input.DefaultModel.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return $"The default model '{input.DefaultModel}' is not among the allowed models.";
            }
        }

        return null;
    }

    #endregion

    #region 投影

    private UsageLimitDefaultsDto BuildDefaults(AiUsagePolicy? row)
    {
        var options = _limitOptions.CurrentValue;
        return new UsageLimitDefaultsDto
        {
            Policy = ToDto(row),
            Configuration = new UsageLimitConfigurationDto
            {
                SessionLimitUsd = options.SessionLimitUsd,
                WeeklyLimitUsd = options.WeeklyLimitUsd,
                AllowedModels = options.AllowedModels?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? [],
                DefaultModel = options.DefaultModel,
                SessionWindowMinutes = (int)Math.Round(options.SessionWindow.TotalMinutes),
                WeeklyWindowMinutes = (int)Math.Round(options.WeeklyWindow.TotalMinutes),
                WarningThreshold = options.WarningThreshold
            },
            Effective = Effective(Merge(row, ConfigurationLayer())),
            Catalog = GetCatalog().ToList()
        };
    }

    private UserUsageLimitsDto BuildUserPolicy(Guid userId, AiUsagePolicy? row, AiUsagePolicy? defaults)
    {
        var lower = Merge(defaults, ConfigurationLayer());
        return new UserUsageLimitsDto
        {
            UserId = userId,
            HasCustomPolicy = row is not null,
            Policy = ToDto(row),
            Defaults = Effective(lower),
            Effective = Effective(Merge(row, lower)),
            Catalog = GetCatalog().ToList()
        };
    }

    private static UsagePolicyDto ToDto(AiUsagePolicy? row)
        => row is null
            ? new UsagePolicyDto()
            : new UsagePolicyDto
            {
                SessionLimit = new UsageLimitSettingDto { Mode = row.SessionLimitMode, AmountUsd = row.SessionLimitUsd },
                WeeklyLimit = new UsageLimitSettingDto { Mode = row.WeeklyLimitMode, AmountUsd = row.WeeklyLimitUsd },
                AllowedModels = ReadModels(row.AllowedModelsJson),
                DefaultModel = row.DefaultModel,
                ModelEfforts = ReadModelEfforts(row.ModelEffortsJson),
                Note = row.Note,
                LastModificationTime = row.LastModificationTime ?? row.CreationTime
            };

    #endregion
}
