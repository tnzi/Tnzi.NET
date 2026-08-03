namespace Tnzi.AI.Services;

/// <summary>
/// 子 Agent 类型定义管理服务实现。
/// </summary>
public class SubAgentTypeService : ApplicationService, ISubAgentTypeService
{
    private readonly IRepository<SubAgentType, Guid> _repository;
    private readonly ISubAgentRegistry _registry;

    public SubAgentTypeService(
        IServiceProvider serviceProvider,
        IRepository<SubAgentType, Guid> repository,
        ISubAgentRegistry registry)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _registry = Check.NotNull(registry);
    }

    /// <inheritdoc />
    public async Task<Result<List<SubAgentTypeDto>>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.AsQueryable()
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ProjectTo<SubAgentType, SubAgentTypeDto>()
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <inheritdoc />
    public async Task<Result<SubAgentTypeDto>> CreateAsync(SubAgentTypeInputDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = input.MapTo<SubAgentType>();
        await _repository.InsertAsync(entity, cancellationToken);
        await ReloadRegistryAsync(cancellationToken);

        return Ok(entity.MapTo<SubAgentTypeDto>());
    }

    /// <inheritdoc />
    public async Task<Result<SubAgentTypeDto>> UpdateAsync(Guid id, SubAgentTypeInputDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail<SubAgentTypeDto>("Sub-agent type not found.", 404, ErrorCodes.SubAgentTypeNotFound);

        entity.Name = input.Name;
        entity.Description = input.Description;
        entity.ToolGroups = input.ToolGroups;
        entity.ExcludedToolGroups = input.ExcludedToolGroups;
        entity.MaxTurns = input.MaxTurns;
        entity.Instructions = input.Instructions;
        entity.DefaultModel = input.DefaultModel;
        entity.DefaultApprovalMode = input.DefaultApprovalMode;
        entity.CapabilityTags = input.CapabilityTags;
        entity.IsEnabled = input.IsEnabled;

        await _repository.UpdateAsync(entity, cancellationToken);
        await ReloadRegistryAsync(cancellationToken);

        return Ok(entity.MapTo<SubAgentTypeDto>());
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Sub-agent type not found.", 404, ErrorCodes.SubAgentTypeNotFound);

        await _repository.DeleteAsync(entity, cancellationToken);
        await ReloadRegistryAsync(cancellationToken);

        return Ok();
    }

    /// <summary>
    /// 从数据库整表重载已启用的类型到注册表（不能逐条 Unregister，理由见接口注释）。
    /// </summary>
    private Task ReloadRegistryAsync(CancellationToken cancellationToken)
        => _registry.LoadFromStoreAsync(_repository, cancellationToken);
}
