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

        if (await NameTakenAsync(input.Name, excludeId: null, cancellationToken))
            return NameConflict(input.Name);

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

        if (await NameTakenAsync(input.Name, excludeId: id, cancellationToken))
            return NameConflict(input.Name);

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
    /// 本租户是否已有同名类型（不分大小写：注册表按名查找不分大小写，两个只差大小写的类型在桶里会互相覆盖）。
    /// 先查再写，让重名得到 409，而不是撞上 (TenantId, Name) 唯一索引变成 500。
    /// </summary>
    private Task<bool> NameTakenAsync(string? name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalized = name?.Trim().ToLower();
        if (string.IsNullOrEmpty(normalized))
            return Task.FromResult(false);

        return _repository.AsQueryable()
            .AnyAsync(e => e.Name.ToLower() == normalized && (excludeId == null || e.Id != excludeId), cancellationToken);
    }

    private Result<SubAgentTypeDto> NameConflict(string name)
        => Fail<SubAgentTypeDto>($"A sub-agent type named '{name}' already exists.", 409, ErrorCodes.SubAgentTypeAlreadyExists);

    /// <summary>
    /// 把本次写入刷进当前工作单元，再从数据库整桶重载已启用的类型到注册表（不能逐条 Unregister，理由见接口注释）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 先 flush：宿主开着全局工作单元时仓储延迟保存，EF 查询看不见变更跟踪器里尚未保存的增改删，
    /// 不 flush 就重载 = 注册表回到请求之前的样子（新建的类型 spawn 时 404，禁用 / 删除的类型照样能 spawn），
    /// 而接口返回 200。flush 走工作单元而不是绕开事务直接落库，重载与写入在同一条连接上，看得见本事务的未提交写入；
    /// 代价是请求随后回滚时注册表里会留着这次的改动，直到下一次 CRUD 或重启（与权限规则的刷新同一取舍）。
    /// </para>
    /// <para>
    /// 只重载当前租户的桶：注册表是进程级单例，仓储受本作用域的租户过滤，
    /// 不带租户键的整表重载会清掉其它租户的类型、并把本租户的定义暴露给全体。
    /// </para>
    /// </remarks>
    private async Task ReloadRegistryAsync(CancellationToken cancellationToken)
    {
        await _repository.SaveChangesAsync(cancellationToken);
        await _registry.LoadTenantFromStoreAsync(
            _repository, SubAgentTenantKey.From(ServiceProvider.GetService<ICurrentTenant>()?.Id), cancellationToken);
    }
}
