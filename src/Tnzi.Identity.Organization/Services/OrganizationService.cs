
namespace Tnzi.Identity.Organization.Services;

// ★ 这一行必须写在**命名空间体内**，不能挪进 GlobalUsings.cs。
// 本程序集叫 Tnzi.Identity.Organization，于是命名空间 Tnzi.Identity 下多了一个名叫
// `Organization` 的成员；C# 每一层先查命名空间成员、再查类型、最后才查该层 using，
// 而 global using 属于编译单元层（最外层）。裸写 `Organization` 会在 Tnzi.Identity
// 那一层命中命名空间并报 CS0118。写在这里，它属于本命名空间这一层，先于外层命中类型。
// 详见 GlobalUsings.cs 顶部的说明。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 组织架构服务实现
/// </summary>
public class OrganizationService : ApplicationService, IOrganizationService
{
    private static readonly TimeSpan OrganizationTreeCacheExpiration = TimeSpan.FromHours(1);
    private static readonly TimeSpan OrganizationCacheExpiration = TimeSpan.FromMinutes(30);

    private readonly IRepository<Organization, Guid> _organizationRepository;
    private readonly DbContext? _dbContext;
    private readonly IEventBus? _eventBus;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICache? _cache;
    private readonly UserManager<User>? _userManager;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化一个<see cref="OrganizationService"/>类型的新实例
    /// </summary>
    public OrganizationService(
        IRepository<Organization, Guid> organizationRepository,
        IServiceProvider serviceProvider,
        DbContext? dbContext = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ICache? cache = null,
        UserManager<User>? userManager = null)
        : base(serviceProvider)
    {
        _organizationRepository = Check.NotNull(organizationRepository);
        _dbContext = dbContext;
        _eventBus = eventBus;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _cache = cache;
        _userManager = userManager;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    public async Task<Result<IEnumerable<OrganizationTreeNodeDto>>> GetTreeAsync()
    {
        // 尝试从缓存获取组织树
        var cacheKey = GetOrganizationTreeCacheKey();
        if (_cache != null)
        {
            var cachedTree = await _cache.GetAsync<List<OrganizationTreeNodeDto>>(cacheKey);
            if (cachedTree != null)
            {
                return Ok<IEnumerable<OrganizationTreeNodeDto>>(cachedTree);
            }
        }

        var dtos = await _organizationRepository
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.CreationTime)
            .ProjectTo<Organization, OrganizationTreeItemDto>()
            .ToListAsync();

        var tree = BuildTree(dtos);

        // 缓存组织树（1小时）
        if (_cache != null)
        {
            await _cache.SetAsync(cacheKey, tree, OrganizationTreeCacheExpiration);
        }
        return Ok<IEnumerable<OrganizationTreeNodeDto>>(tree);
    }

    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    public async Task<Result<OrganizationDto>> GetByIdAsync(Guid id)
    {
        // 尝试从缓存获取
        if (_cache != null)
        {
            var cacheKey = GetOrganizationCacheKey(id);
            var cachedOrg = await _cache.GetAsync<OrganizationDto>(cacheKey);
            if (cachedOrg != null)
            {
                return Ok(cachedOrg);
            }
        }

        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail<OrganizationDto>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var dto = organization.MapTo<OrganizationDto>();

        // 存入缓存（30分钟过期）
        if (_cache != null)
        {
            var cacheKey = GetOrganizationCacheKey(id);
            await _cache.SetAsync(cacheKey, dto, OrganizationCacheExpiration);
        }

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids)
    {
        Check.NotNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // 一页用户最多一次 IN 查询：拆分前这一列来自 User → Organization 的 LEFT JOIN，
        // 拆分后走这里。刻意去重（同一组织下多个用户只问一次），且**不缓存** ——
        // 组织名改了要立刻反映在用户列表上，而单条组织的缓存由 GetByIdAsync 那条路径管。
        var distinct = ids.Distinct().ToList();
        var rows = await _organizationRepository
            .Where(o => distinct.Contains(o.Id) && !o.IsDeleted)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    /// <summary>
    /// 创建组织
    /// </summary>
    public async Task<Result<OrganizationDto>> CreateAsync(CreateOrganizationDto input)
    {
        // 验证组织代码唯一性
        if (!string.IsNullOrEmpty(input.Code))
        {
            var exists = await _organizationRepository
                .AnyAsync(o => o.Code == input.Code && !o.IsDeleted);
            if (exists)
            {
                return Fail<OrganizationDto>($"Organization with code '{input.Code}' already exists.", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 计算层级信息
        var (parentPath, level) = await CalculatePathAndLevelAsync(input.ParentId);

        var organization = input.MapTo<Organization>();
        organization.IsEnabled = true;
        // ★ Path 的每一段都是**实体 Id**（GetAllParentsAsync 把路径段解析成祖先 Id；MoveAsync 也写真 Id）。
        //   Id 由 SaveChanges 在值为默认值时才生成、且不回填到这里，所以要在拼路径之前先自己定下来 ——
        //   此前这里写的是另一枚随机 GUID，祖先查询自初始提交起恒为空。
        //   经仓储的 NewId() 而不是 SequentialGuid.NewGuid()：与保存时同一套规则（按 provider 选排列、认注册的生成器）。
        organization.Id = NewOrganizationId();
        organization.Path = $"{parentPath}{organization.Id}/";
        organization.Level = level + 1;

        try
        {
            await _organizationRepository.InsertAsync(organization);
        }
        catch (DbUpdateException ex)
        {
            // 处理并发创建时的唯一约束冲突（Code 字段）
            if (ex.IsUniqueConstraintViolation())
            {
                return Fail<OrganizationDto>(
                    $"Organization with code '{input.Code}' already exists.",
                    409,
                    ErrorCodes.VALIDATION_ERROR);
            }
            // 其他数据库错误，重新抛出
            throw;
        }

        // 清除缓存
        await ClearOrganizationCacheAsync();

        // 发布组织创建事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new OrganizationCreatedEvent
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                ParentId = organization.ParentId,
                CreatorId = _currentUser?.Id
            }, cancellationToken: default);
        }

        var dto = organization.MapTo<OrganizationDto>();
        LogInformation("Organization created: {Name} (ID: {Id})", organization.Name, organization.Id);
        return Ok(dto);
    }

    /// <summary>
    /// 更新组织
    /// </summary>
    public async Task<Result<OrganizationDto>> UpdateAsync(Guid id, UpdateOrganizationDto input)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail<OrganizationDto>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 验证组织代码唯一性（排除自己）
        if (!string.IsNullOrEmpty(input.Code) &&
            (organization.Code == null || !string.Equals(input.Code, organization.Code, StringComparison.OrdinalIgnoreCase)))
        {
            var exists = await _organizationRepository
                .AnyAsync(o => o.Code == input.Code && o.Id != id && !o.IsDeleted);
            if (exists)
            {
                return Fail<OrganizationDto>($"Organization with code '{input.Code}' already exists.", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 记录变更前的值用于事件字段对比
        var originalName = organization.Name;
        var originalCode = organization.Code;
        var originalRemark = organization.Remark;
        var originalSortOrder = organization.SortOrder;
        var originalIsEnabled = organization.IsEnabled;

        input.MapTo(organization);

        try
        {
            await _organizationRepository.UpdateAsync(organization);
        }
        catch (DbUpdateException ex)
        {
            // 处理并发更新时的唯一约束冲突（Code 字段）
            if (ex.IsUniqueConstraintViolation())
            {
                return Fail<OrganizationDto>(
                    $"Organization with code '{input.Code}' already exists.",
                    409,
                    ErrorCodes.VALIDATION_ERROR);
            }
            // 其他数据库错误，重新抛出
            throw;
        }

        // 清除缓存
        await ClearOrganizationCacheAsync(id);

        // 发布组织更新事件
        if (_eventBus != null)
        {
            var updatedFields = new List<string>();
            if (!string.Equals(input.Name, originalName, StringComparison.Ordinal)) updatedFields.Add(nameof(Organization.Name));
            if (!string.Equals(input.Code, originalCode, StringComparison.OrdinalIgnoreCase)) updatedFields.Add(nameof(Organization.Code));
            if (!string.Equals(input.Remark, originalRemark, StringComparison.Ordinal)) updatedFields.Add(nameof(Organization.Remark));
            if (input.SortOrder != originalSortOrder) updatedFields.Add(nameof(Organization.SortOrder));
            if (input.IsEnabled != originalIsEnabled) updatedFields.Add(nameof(Organization.IsEnabled));

            await _eventBus.PublishAsync(new OrganizationUpdatedEvent
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                UpdatedFields = updatedFields,
                LastModifierId = _currentUser?.Id
            }, cancellationToken: default);
        }

        var dto = organization.MapTo<OrganizationDto>();
        LogInformation("Organization updated: {Name} (ID: {Id})", organization.Name, organization.Id);
        return Ok(dto);
    }

    /// <summary>
    /// 删除组织
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 检查是否有子组织
        var hasChildren = await _organizationRepository
            .AnyAsync(o => o.ParentId == id && !o.IsDeleted);
        if (hasChildren)
        {
            return Fail("Cannot delete organization with children.", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await _organizationRepository.DeleteAsync(id);

        // 清除缓存
        await ClearOrganizationCacheAsync(id);

        // 发布组织删除事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new OrganizationDeletedEvent
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                DeletedBy = _currentUser?.Id
            }, cancellationToken: default);
        }

        LogInformation("Organization deleted: {Name} (ID: {Id})", organization.Name, organization.Id);
        return Ok();
    }

    /// <summary>
    /// 移动组织到新的父组织
    /// </summary>
    public async Task<Result> MoveAsync(Guid id, Guid? newParentId)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 检查是否移动到自己的子组织下（防止循环引用）
        if (newParentId.HasValue)
        {
            var allChildren = await GetAllChildrenIdsAsync(id);
            if (allChildren.Contains(newParentId.Value))
            {
                return Fail("Cannot move organization to its own child.", 400, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 计算新的层级信息
        var (parentPath, level) = await CalculatePathAndLevelAsync(newParentId);
        var oldPath = organization.Path;
        if (string.IsNullOrEmpty(oldPath))
        {
            return Fail($"Organization {id} has null Path.", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        organization.ParentId = newParentId;
        organization.Path = $"{parentPath}{id}/";
        organization.Level = level + 1;

        // 先更新父组织，再更新子组织
        // 这样即使子组织更新失败，父组织已经更新，数据状态更安全
        // 如果先更新子组织再更新父组织，父组织更新失败时会导致数据不一致
        await _organizationRepository.UpdateAsync(organization);

        // 更新所有子组织的路径和层级
        await UpdateDescendantsPathAsync(id, oldPath, organization.Path);

        // 清除缓存
        await ClearOrganizationCacheAsync(id);

        LogInformation("Organization moved: {Name} (ID: {Id}) to parent {NewParentId}", organization.Name, organization.Id, newParentId?.ToString() ?? "root");
        return Ok();
    }

    /// <summary>
    /// 获取组织的所有子组织（包括子子组织）
    /// </summary>
    public async Task<Result<IEnumerable<OrganizationDto>>> GetAllChildrenAsync(Guid id)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail<IEnumerable<OrganizationDto>>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var path = organization.Path ?? $"/{id}/";
        var children = await _organizationRepository
            .Where(o => o.Path != null && o.Path.StartsWith(path) && o.Id != id && !o.IsDeleted)
            .OrderBy(o => o.SortOrder)
            .ProjectTo<Organization, OrganizationDto>()
            .ToListAsync();

        return Ok<IEnumerable<OrganizationDto>>(children);
    }

    /// <summary>
    /// 获取组织的所有父组织（包括父父组织）
    /// </summary>
    public async Task<Result<IEnumerable<OrganizationDto>>> GetAllParentsAsync(Guid id)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail<IEnumerable<OrganizationDto>>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (string.IsNullOrEmpty(organization.Path))
        {
            return Ok<IEnumerable<OrganizationDto>>(Enumerable.Empty<OrganizationDto>());
        }

        // 从路径中提取所有父组织ID
        var pathParts = organization.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentIds = pathParts
            .Where(p => Guid.TryParse(p, out _))
            .Select(Guid.Parse)
            .Where(pid => pid != id)
            .ToList();

        if (!parentIds.Any())
        {
            return Ok<IEnumerable<OrganizationDto>>(Enumerable.Empty<OrganizationDto>());
        }

        var parents = await _organizationRepository
            .Where(o => parentIds.Contains(o.Id) && !o.IsDeleted)
            .OrderBy(o => o.Level)
            .ProjectTo<Organization, OrganizationDto>()
            .ToListAsync();

        return Ok<IEnumerable<OrganizationDto>>(parents);
    }

    /// <summary>
    /// 预先定下新组织的 Id（路径段就是它）：经仓储与保存时同一套规则；拿到默认值一律拒绝 ——
    /// 默认值会让 SaveChanges 另发一枚，Path 末段与实体 Id 从此对不上，正是修过的那个缺陷。
    /// </summary>
    private Guid NewOrganizationId()
    {
        var id = _organizationRepository.NewId();
        return id != Guid.Empty
            ? id
            : throw new InvalidOperationException("The organization repository returned an empty id; the organization path cannot be built.");
    }

    /// <summary>
    /// 计算父组织的路径和层级
    /// </summary>
    private async Task<(string parentPath, int level)> CalculatePathAndLevelAsync(Guid? parentId)
    {
        if (!parentId.HasValue)
        {
            return ("/", 0);
        }

        var parent = await _organizationRepository.GetAsync(parentId.Value);
        if (parent == null)
        {
            return ("/", 0);
        }

        return (parent.Path ?? $"/{parentId.Value}/", parent.Level);
    }

    /// <summary>
    /// 更新所有后继组织的路径和层级
    /// </summary>
    private async Task UpdateDescendantsPathAsync(Guid organizationId, string oldPath, string newPath)
    {
        // 一次性获取所有后代组织
        var descendants = await _organizationRepository
            .Where(o => o.Path != null && o.Path.StartsWith(oldPath) && o.Id != organizationId && !o.IsDeleted)
            .ToListAsync();

        if (!descendants.Any()) return;

        foreach (var desc in descendants)
        {
            if (desc.Path != null)
            {
                desc.Path = desc.Path.Replace(oldPath, newPath, StringComparison.Ordinal);
                desc.Level = desc.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        await _organizationRepository.UpdateManyAsync(descendants);
    }

    /// <summary>
    /// 获取所有子组织ID（基于路径，避免递归）
    /// </summary>
    private async Task<List<Guid>> GetAllChildrenIdsAsync(Guid id)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null || string.IsNullOrEmpty(organization.Path))
        {
            return new List<Guid>();
        }

        return await _organizationRepository
            .Where(o => o.Path != null && o.Path.StartsWith(organization.Path) && o.Id != id && !o.IsDeleted)
            .Select(o => o.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 构建组织树
    /// </summary>
    private static List<OrganizationTreeNodeDto> BuildTree(List<OrganizationTreeItemDto> allOrganizations)
    {
        if (allOrganizations == null || allOrganizations.Count == 0)
        {
            return new List<OrganizationTreeNodeDto>();
        }

        var nodes = allOrganizations.MapToList<OrganizationTreeNodeDto>();
        return TreeHelper.ToTree(nodes, o => o.Id, o => o.ParentId, (p, c) => p.Children.Add(c)).ToList();
    }



    public async Task<Result<IEnumerable<OrganizationDto>>> CreateManyAsync(IEnumerable<CreateOrganizationDto> inputs)
    {
        var inputList = inputs.ToList();
        var organizations = new List<Organization>();

        // 收集所有父节点ID并批量查询，消除 N+1
        var parentIds = inputList.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).Distinct().ToList();
        var parentsMap = parentIds.Any()
            ? (await _organizationRepository.Where(o => parentIds.Contains(o.Id)).ToListAsync()).ToDictionary(o => o.Id)
            : new Dictionary<Guid, Organization>();

        foreach (var input in inputList)
        {
            string parentPath = "/";
            int level = 0;

            if (input.ParentId.HasValue && parentsMap.TryGetValue(input.ParentId.Value, out var parent))
            {
                parentPath = parent.Path ?? $"/{parent.Id}/";
                level = parent.Level;
            }

            var organization = input.MapTo<Organization>();
            organization.IsEnabled = true;
            // 同 CreateAsync：路径段必须是实体自己的 Id，且与保存时同一套生成规则。
            organization.Id = NewOrganizationId();
            organization.Path = $"{parentPath}{organization.Id}/";
            organization.Level = level + 1;
            organizations.Add(organization);
        }

        try
        {
            await _organizationRepository.InsertManyAsync(organizations);
        }
        catch (DbUpdateException ex)
        {
            // 处理并发创建时的唯一约束冲突（Code 字段）
            if (ex.IsUniqueConstraintViolation())
            {
                // 尝试找出冲突的组织代码
                var conflictingCodes = inputList
                    .Where(i => !string.IsNullOrEmpty(i.Code))
                    .Select(i => i.Code!)
                    .ToList();

                return Fail<IEnumerable<OrganizationDto>>(
                    $"One or more organizations with duplicate codes already exist. Codes: {string.Join(", ", conflictingCodes)}",
                    409,
                    ErrorCodes.VALIDATION_ERROR);
            }
            // 其他数据库错误，重新抛出
            throw;
        }

        // 清除缓存
        await ClearOrganizationCacheAsync();

        // 发布组织创建事件
        if (_eventBus != null)
        {
            foreach (var organization in organizations)
            {
                await _eventBus.PublishAsync(new OrganizationCreatedEvent
                {
                    OrganizationId = organization.Id,
                    OrganizationName = organization.Name,
                    ParentId = organization.ParentId,
                    CreatorId = _currentUser?.Id
                }, cancellationToken: default);
            }
        }

        var dtos = organizations.MapToList<OrganizationDto>();
        LogInformation("Created {Count} organizations", organizations.Count);
        return Ok<IEnumerable<OrganizationDto>>(dtos);
    }

    public async Task<Result<IEnumerable<OrganizationDto>>> UpdateManyAsync(IEnumerable<(Guid Id, UpdateOrganizationDto Dto)> inputs)
    {
        var inputList = inputs.ToList();
        var organizations = new List<Organization>();

        // 批量查找组织
        var ids = inputList.Select(i => i.Id).ToList();
        var existingOrganizations = await _organizationRepository
            .Where(o => ids.Contains(o.Id))
            .ToListAsync();

        var orgDict = existingOrganizations.ToDictionary(o => o.Id);

        // 批量获取需要验证的代码，消除 N+1
        var codesToCheck = inputList
            .Where(i => !string.IsNullOrEmpty(i.Dto.Code) && (!orgDict.ContainsKey(i.Id) || i.Dto.Code != orgDict[i.Id].Code))
            .Select(i => i.Dto.Code!)
            .Distinct()
            .ToList();

        var existingCodesMap = codesToCheck.Any()
            ? (await _organizationRepository.Where(o => codesToCheck.Contains(o.Code!) && !o.IsDeleted).ToListAsync())
                .GroupBy(o => o.Code!)
                .ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToList())
            : new Dictionary<string, List<Guid>>();

        // 批量更新
        foreach (var (id, dto) in inputList)
        {
            if (!orgDict.TryGetValue(id, out var organization))
            {
                return Fail<IEnumerable<OrganizationDto>>($"Organization {id} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
            }

            // 验证组织代码唯一性（使用预获取的数据）
            if (!string.IsNullOrEmpty(dto.Code) && dto.Code != organization.Code)
            {
                if (existingCodesMap.TryGetValue(dto.Code, out var conflictingIds) && conflictingIds.Any(cid => cid != id))
                {
                    return Fail<IEnumerable<OrganizationDto>>($"Organization with code '{dto.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
                }
            }

            dto.MapTo(organization);
            organizations.Add(organization);
        }

        try
        {
            await _organizationRepository.UpdateManyAsync(organizations);
        }
        catch (DbUpdateException ex)
        {
            // 处理并发更新时的唯一约束冲突（Code 字段）
            if (ex.IsUniqueConstraintViolation())
            {
                // 尝试找出冲突的组织代码
                var conflictingCodes = inputList
                    .Where(i => !string.IsNullOrEmpty(i.Dto.Code))
                    .Select(i => i.Dto.Code!)
                    .Distinct()
                    .ToList();

                return Fail<IEnumerable<OrganizationDto>>(
                    $"One or more organizations with duplicate codes already exist. Codes: {string.Join(", ", conflictingCodes)}",
                    409,
                    ErrorCodes.VALIDATION_ERROR);
            }
            // 其他数据库错误，重新抛出
            throw;
        }

        // 清除缓存
        foreach (var organization in organizations)
        {
            await ClearOrganizationCacheAsync(organization.Id);
        }

        // 发布组织更新事件
        if (_eventBus != null)
        {
            foreach (var (id, dto) in inputList)
            {
                var organization = orgDict[id];
                var updatedFields = new List<string>();
                if (dto.Name != organization.Name) updatedFields.Add(nameof(Organization.Name));
                if (dto.Code != organization.Code) updatedFields.Add(nameof(Organization.Code));
                if (dto.Remark != organization.Remark) updatedFields.Add(nameof(Organization.Remark));
                if (dto.SortOrder != organization.SortOrder) updatedFields.Add(nameof(Organization.SortOrder));
                if (dto.IsEnabled != organization.IsEnabled) updatedFields.Add(nameof(Organization.IsEnabled));

                await _eventBus.PublishAsync(new OrganizationUpdatedEvent
                {
                    OrganizationId = organization.Id,
                    OrganizationName = organization.Name,
                    UpdatedFields = updatedFields,
                    LastModifierId = _currentUser?.Id
                }, cancellationToken: default);
            }
        }

        var dtos = organizations.MapToList<OrganizationDto>();
        LogInformation("Updated {Count} organizations", organizations.Count);
        return Ok<IEnumerable<OrganizationDto>>(dtos);
    }

    public async Task<Result> DeleteManyAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();

        // 批量检查是否有子组织，避免N+1查询
        var organizationsWithChildren = await _organizationRepository
            .Where(o => idList.Contains(o.ParentId ?? Guid.Empty) && !o.IsDeleted)
            .Select(o => o.ParentId!.Value)
            .Distinct()
            .ToListAsync();

        if (organizationsWithChildren.Any())
        {
            var conflictingIds = string.Join(", ", organizationsWithChildren);
            return Fail($"Cannot delete organizations with children. Organization IDs: {conflictingIds}", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var organizations = await _organizationRepository
            .Where(o => idList.Contains(o.Id))
            .ToListAsync();

        await _organizationRepository.DeleteManyAsync(organizations);

        // 清除缓存
        foreach (var organization in organizations)
        {
            await ClearOrganizationCacheAsync(organization.Id);
        }

        // 发布组织删除事件
        if (_eventBus != null)
        {
            foreach (var organization in organizations)
            {
                await _eventBus.PublishAsync(new OrganizationDeletedEvent
                {
                    OrganizationId = organization.Id,
                    OrganizationName = organization.Name,
                    DeletedBy = _currentUser?.Id
                }, cancellationToken: default);
            }
        }

        LogInformation("Deleted {Count} organizations", organizations.Count);
        return Ok();
    }

    public async Task<Result<OrganizationStatisticsDto>> GetStatisticsAsync(Guid id)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail<OrganizationStatisticsDto>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 获取子组织总数 (直接 Count，避免加载 DTO)
        var organizationPath = organization.Path ?? string.Empty;
        var totalChildren = await _organizationRepository.CountAsync(o => o.Path != null && o.Path.StartsWith(organizationPath) && o.Id != id && !o.IsDeleted);

        // 统计用户数
        int directUsers = 0;
        int totalUsers = 0;

        if (_dbContext != null)
        {
            // 直接属于该组织的用户数
            directUsers = await _dbContext.Set<User>()
                .Where(u => u.OrganizationId == id && !u.IsDeleted)
                .CountAsync();

            // 包括子组织的所有用户数（使用 Join 和 Path 优化）
            var orgPath = organization.Path ?? string.Empty;
            totalUsers = await _dbContext.Set<User>()
                .Join(_dbContext.Set<Organization>(), u => u.OrganizationId, o => o.Id, (u, o) => new { u, o })
                .Where(x => !x.u.IsDeleted && x.o.Path != null && x.o.Path.StartsWith(orgPath))
                .CountAsync();
        }

        var statistics = new OrganizationStatisticsDto
        {
            DirectChildren = await _organizationRepository
                .CountAsync(o => o.ParentId == id && !o.IsDeleted),
            TotalChildren = totalChildren,
            DirectUsers = directUsers,
            TotalUsers = totalUsers
        };

        return Ok(statistics);
    }

    /// <summary>
    /// 清除组织缓存
    /// </summary>
    private async Task ClearOrganizationCacheAsync(Guid? organizationId = null)
    {
        if (_cache == null)
        {
            return;
        }

        // 清除组织树缓存
        await _cache.RemoveAsync(GetOrganizationTreeCacheKey());

        // 如果指定了组织ID，清除该组织的缓存
        if (organizationId.HasValue)
        {
            var cacheKey = GetOrganizationCacheKey(organizationId.Value);
            await _cache.RemoveAsync(cacheKey);
        }
    }

    private string GetOrganizationTreeCacheKey()
    {
        if (!_multiTenancyEnabled)
        {
            return CacheKeys.Identity.OrganizationTree;
        }

        var tenantPart = _currentTenant?.Id?.ToString() ?? "host";
        return $"{CacheKeys.Identity.OrganizationTree}:{tenantPart}";
    }

    private string GetOrganizationCacheKey(Guid organizationId)
    {
        if (!_multiTenancyEnabled)
        {
            return CacheKeys.Identity.Organization(organizationId);
        }

        var tenantPart = _currentTenant?.Id?.ToString() ?? "host";
        return $"{CacheKeys.Identity.Organization(organizationId)}:{tenantPart}";
    }

    /// <summary>
    /// 按 id 取账号，且只取当前租户范围内的（口径见 <see cref="UserTenantScope"/>）。
    /// 组织本身受全局租户过滤器管，但用户不受 —— 少了这一道，租户 A 的管理员能把
    /// 别家租户的账号挂进自己的组织。范围外与不存在同样返回 <c>null</c>，调用方答 404。
    /// </summary>
    private async Task<User?> FindScopedUserAsync(Guid userId)
    {
        // FindByGuidAsync 是核心的 internal 扩展，跨程序集用不了；它就是这一行。
        var user = await _userManager!.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return null;
        }

        var scope = UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, _currentUser ?? CurrentUser);
        if (scope.Contains(user))
        {
            return user;
        }

        LogWarning(
            "Rejected a cross-tenant organization assignment: user {UserId} belongs to tenant {UserTenantId} but the request is scoped to tenant {TenantId}.",
            user.Id, user.TenantId, scope.TenantId);
        return null;
    }

    /// <summary>
    /// 分配用户到组织
    /// </summary>
    public async Task<Result> AssignUserToOrganizationAsync(Guid userId, Guid organizationId)
    {
        if (_userManager == null)
        {
            return Fail("UserManager is not available", 500, ErrorCodes.CONFIGURATION_ERROR);
        }

        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 验证组织是否存在
        var organizationResult = await GetByIdAsync(organizationId);
        if (!organizationResult.Succeeded)
        {
            return Fail(organizationResult.Message ?? "Organization not found", organizationResult.Code ?? 404, organizationResult.ErrorCode);
        }
        var organization = organizationResult.Data;
        if (organization == null)
        {
            return Fail("Organization not found", 404, ErrorCodes.IDENTITY_ORGANIZATION_NOT_FOUND);
        }

        user.OrganizationId = organizationId;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail($"Failed to assign user to organization: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_ORGANIZATION_ERROR);
        }

        // 发布用户分配到组织事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserAssignedToOrganizationEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                OrganizationId = organizationId,
                OrganizationName = organization!.Name ?? string.Empty,
                AssignedBy = _currentUser?.Id
            }, cancellationToken: default);
        }

        // 清除用户缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User {UserName} assigned to organization {OrgName} (ID: {OrgId})", user.UserName ?? string.Empty, organization!.Name ?? string.Empty, organizationId);
        return Ok();
    }

    /// <summary>
    /// 从组织移除用户
    /// </summary>
    public async Task<Result> RemoveUserFromOrganizationAsync(Guid userId)
    {
        if (_userManager == null)
        {
            return Fail("UserManager is not available", 500, ErrorCodes.CONFIGURATION_ERROR);
        }

        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var oldOrganizationId = user.OrganizationId;
        user.OrganizationId = null;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail($"Failed to remove user from organization: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_ORGANIZATION_ERROR);
        }

        // 发布用户从组织移除事件
        if (_eventBus != null && oldOrganizationId.HasValue)
        {
            var organizationResult = await GetByIdAsync(oldOrganizationId.Value);
            var organizationName = organizationResult.Succeeded ? organizationResult.Data?.Name ?? string.Empty : string.Empty;
            await _eventBus.PublishAsync(new UserRemovedFromOrganizationEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                OrganizationId = oldOrganizationId.Value,
                OrganizationName = organizationName,
                RemovedBy = _currentUser?.Id
            }, cancellationToken: default);
        }

        // 清除用户缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User {UserName} removed from organization (ID: {OrgId})", user.UserName ?? string.Empty, oldOrganizationId?.ToString() ?? string.Empty);
        return Ok();
    }

    /// <summary>
    /// 根据名称或代码模糊搜索组织
    /// </summary>
    public async Task<Result<IEnumerable<OrganizationDto>>> SearchAsync(string keyword, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Ok<IEnumerable<OrganizationDto>>(Enumerable.Empty<OrganizationDto>());
        }

        var lowerKeyword = keyword.ToLower();

        var results = await _organizationRepository
            .Where(o => !o.IsDeleted &&
                (o.Name.ToLower().Contains(lowerKeyword) ||
                 (o.Code != null && o.Code.ToLower().Contains(lowerKeyword))))
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Name)
            .Take(maxResults)
            .ProjectTo<Organization, OrganizationDto>()
            .ToListAsync();

        return Ok<IEnumerable<OrganizationDto>>(results);
    }

    /// <summary>
    /// 更新组织排序
    /// </summary>
    public async Task<Result> UpdateSortOrderAsync(Guid id, int newSortOrder)
    {
        var organization = await _organizationRepository.GetAsync(id);
        if (organization == null)
        {
            return Fail("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        organization.SortOrder = newSortOrder;
        await _organizationRepository.UpdateAsync(organization);

        // 清除缓存（排序变更影响树形结构）
        await ClearOrganizationCacheAsync(id);

        LogInformation("Organization sort order updated: {Name} (ID: {Id}) to {SortOrder}", organization.Name, organization.Id, newSortOrder);
        return Ok();
    }

    /// <summary>
    /// 批量更新组织排序
    /// </summary>
    public async Task<Result> BatchUpdateSortOrderAsync(IEnumerable<(Guid Id, int SortOrder)> updates)
    {
        var updateList = updates.ToList();
        if (!updateList.Any())
        {
            return Ok();
        }

        var ids = updateList.Select(u => u.Id).ToList();
        var organizations = await _organizationRepository
            .Where(o => ids.Contains(o.Id))
            .ToListAsync();

        if (organizations.Count != updateList.Count)
        {
            var foundIds = organizations.Select(o => o.Id).ToHashSet();
            var missingIds = ids.Where(id => !foundIds.Contains(id)).ToList();
            return Fail($"Organizations not found: {string.Join(", ", missingIds)}", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var orgDict = organizations.ToDictionary(o => o.Id);
        foreach (var (id, sortOrder) in updateList)
        {
            orgDict[id].SortOrder = sortOrder;
        }

        await _organizationRepository.UpdateManyAsync(organizations);

        // 清除缓存（排序变更影响树形结构）
        await ClearOrganizationCacheAsync();

        LogInformation("Batch updated sort order for {Count} organizations", updateList.Count);
        return Ok();
    }

    /// <summary>
    /// 重排同一父组织下的组织（拖拽排序）
    /// </summary>
    public async Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var result = await ExecuteInUnitOfWorkAsync(
            ct => _organizationRepository.ReorderAsync(ids, o => o.ParentId == parentId, ct),
            cancellationToken);

        if (!result.Succeeded)
            return Result.Failure(result.Message ?? "Reorder failed.", result.Code ?? 400, result.ErrorCode);

        // 排序变更影响树形结构的缓存快照。
        await ClearOrganizationCacheAsync();

        LogInformation("Reordered {Count} organization(s) under parent {ParentId}", result.Data, parentId);
        return Ok();
    }

    /// <summary>
    /// 获取组织下的用户分页列表
    /// </summary>
    public async Task<Result<IPagedList<UserListItemDto>>> GetUsersAsync(Guid organizationId, PagedQueryDto query, bool includeChildren = false)
    {
        var organization = await _organizationRepository.GetAsync(organizationId);
        if (organization == null)
        {
            return Fail<IPagedList<UserListItemDto>>("Organization not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (_dbContext == null)
        {
            // 500 而不是 503：503 是「暂时不可用」，会让监控与客户端重试逻辑一直重试；
            // 解析不到 DbContext 是装配问题，重试多少次都一样。与本模块缺席时答 501 同一条取舍。
            return Fail<IPagedList<UserListItemDto>>("Database context is not available", 500);
        }

        IQueryable<User> usersQuery;

        if (includeChildren)
        {
            // 获取当前组织及所有子组织的 ID
            var path = organization.Path ?? $"/{organizationId}/";
            var orgIds = await _organizationRepository
                .Where(o => o.Path != null && o.Path.StartsWith(path) && !o.IsDeleted)
                .Select(o => o.Id)
                .ToListAsync();

            usersQuery = _dbContext.Set<User>()
                .Where(u => u.OrganizationId.HasValue && orgIds.Contains(u.OrganizationId.Value) && !u.IsDeleted);
        }
        else
        {
            usersQuery = _dbContext.Set<User>()
                .Where(u => u.OrganizationId == organizationId && !u.IsDeleted);
        }

        // 组织已经按租户过滤，但用户表没有全局过滤器；按同一口径再裁一次。
        usersQuery = UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, _currentUser ?? CurrentUser)
            .Apply(usersQuery)
            .OrderByDescending(u => u.CreationTime);

        var paged = await usersQuery
            .ProjectTo<User, UserListItemDto>()
            .CreateAsync(query);

        // 组织名不再随 User 的导航属性投影出来（拆分后 User 不认识 Organization），
        // 按本页的组织 Id 批量补一次。includeChildren=true 时同一页可能横跨多个组织，
        // 所以不能拿 organization.Name 一填了事。
        await FillOrganizationNamesAsync(paged.Items);

        return Ok(paged);
    }

    /// <summary>
    /// 给一批用户列表项补上组织名（一次批量查询，无 N+1）。
    /// </summary>
    private async Task FillOrganizationNamesAsync(IEnumerable<UserListItemDto> items)
    {
        var rows = items as IReadOnlyCollection<UserListItemDto> ?? items.ToList();
        var ids = rows.Where(u => u.OrganizationId.HasValue)
            .Select(u => u.OrganizationId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var names = await GetNamesAsync(ids);
        foreach (var row in rows)
        {
            if (row.OrganizationId.HasValue && names.TryGetValue(row.OrganizationId.Value, out var name))
            {
                row.OrganizationName = name;
            }
        }
    }
}
