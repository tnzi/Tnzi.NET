
namespace Tnzi.Identity.Services;

/// <summary>
/// 组织架构服务实现
/// </summary>
public class OrganizationService : ApplicationService, IOrganizationService
{
    private static readonly TimeSpan OrganizationTreeCacheExpiration = TimeSpan.FromHours(1);
    private static readonly TimeSpan OrganizationCacheExpiration = TimeSpan.FromMinutes(30);

    private readonly IRepository<Organization, Guid> _organizationRepository;
    private readonly Microsoft.EntityFrameworkCore.DbContext? _dbContext;
    private readonly IEventBus? _eventBus;
    private readonly ICurrentUser? _currentUser;
    private readonly Tnzi.Caching.ICache? _cache;
    private readonly UserManager<User>? _userManager;

    /// <summary>
    /// 初始化一个<see cref="OrganizationService"/>类型的新实例
    /// </summary>
    public OrganizationService(
        IRepository<Organization, Guid> organizationRepository,
        IServiceProvider serviceProvider,
        Microsoft.EntityFrameworkCore.DbContext? dbContext = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        Tnzi.Caching.ICache? cache = null,
        UserManager<User>? userManager = null)
        : base(serviceProvider)
    {
        _organizationRepository = Check.NotNull(organizationRepository);
        _dbContext = dbContext;
        _eventBus = eventBus;
        _currentUser = currentUser;
        _cache = cache;
        _userManager = userManager;
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    public async Task<Result<IEnumerable<OrganizationDto>>> GetTreeAsync()
    {
        // 尝试从缓存获取组织树
        const string cacheKey = CacheKeys.Identity.OrganizationTree;
        if (_cache != null)
        {
            var cachedTree = await _cache.GetAsync<List<OrganizationDto>>(cacheKey);
            if (cachedTree != null)
            {
                return Ok<IEnumerable<OrganizationDto>>(cachedTree);
            }
        }

        var dtos = await _organizationRepository
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.CreationTime)
            .ProjectTo<Organization, OrganizationDto>()
            .ToListAsync();

        var tree = BuildTree(dtos);

        // 缓存组织树（1小时）
        if (_cache != null)
        {
            await _cache.SetAsync(cacheKey, tree, OrganizationTreeCacheExpiration);
        }
        return Ok<IEnumerable<OrganizationDto>>(tree);
    }

    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    public async Task<Result<OrganizationDto>> GetByIdAsync(Guid id)
    {
        // 尝试从缓存获取
        if (_cache != null)
        {
            var cacheKey = CacheKeys.Identity.Organization(id);
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
            var cacheKey = CacheKeys.Identity.Organization(id);
            await _cache.SetAsync(cacheKey, dto, OrganizationCacheExpiration);
        }

        return Ok(dto);
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
        organization.Path = $"{parentPath}{SequentialGuid.NewGuid()}/";
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
        LogInformation($"Organization created: {organization.Name} (ID: {organization.Id})");
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
        LogInformation($"Organization updated: {organization.Name} (ID: {organization.Id})");
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

        LogInformation($"Organization deleted: {organization.Name} (ID: {organization.Id})");
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

        LogInformation($"Organization moved: {organization.Name} (ID: {organization.Id}) to parent {newParentId}");
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
    private List<OrganizationDto> BuildTree(List<OrganizationDto> allOrganizations)
    {
        if (allOrganizations == null || allOrganizations.Count == 0)
        {
            return new List<OrganizationDto>();
        }

        var dic = allOrganizations.ToDictionary(o => o.Id);
        var roots = new List<OrganizationDto>();

        foreach (var org in allOrganizations)
        {
            if (org.ParentId.HasValue && dic.TryGetValue(org.ParentId.Value, out var parent))
            {
                parent.Children ??= new List<OrganizationDto>();
                ((List<OrganizationDto>)parent.Children).Add(org);
            }
            else
            {
                roots.Add(org);
            }
        }

        return roots;
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
            organization.Path = $"{parentPath}{SequentialGuid.NewGuid()}/";
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
        LogInformation($"Created {organizations.Count} organizations");
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
        LogInformation($"Updated {organizations.Count} organizations");
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

        LogInformation($"Deleted {organizations.Count} organizations");
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
        await _cache.RemoveAsync(CacheKeys.Identity.OrganizationTree);

        // 如果指定了组织ID，清除该组织的缓存
        if (organizationId.HasValue)
        {
            var cacheKey = CacheKeys.Identity.Organization(organizationId.Value);
            await _cache.RemoveAsync(cacheKey);
        }
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

        var user = await _userManager.FindByGuidAsync(userId);
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

        LogInformation($"User {user.UserName ?? string.Empty} assigned to organization {organization!.Name ?? string.Empty} (ID: {organizationId})");
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

        var user = await _userManager.FindByGuidAsync(userId);
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

        LogInformation($"User {user.UserName} removed from organization (ID: {oldOrganizationId})");
        return Ok();
    }

}