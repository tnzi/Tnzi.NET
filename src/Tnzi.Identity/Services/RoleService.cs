namespace Tnzi.Identity.Services;

/// <summary>
/// 角色管理服务实现
/// </summary>
public class RoleService : ApplicationService, IRoleService
{
    private readonly RoleManager<Role> _roleManager;
    private readonly DbContext _dbContext;

    public RoleService(RoleManager<Role> roleManager, DbContext dbContext, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _roleManager = Check.NotNull(roleManager);
        _dbContext = Check.NotNull(dbContext);
    }

    public async Task<Result<IEnumerable<RoleDto>>> GetAllAsync()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var roleDtos = roles.MapToList<RoleDto>();
        return Ok<IEnumerable<RoleDto>>(roleDtos);
    }

    public async Task<Result<IPagedList<RoleDto>>> GetPagedListAsync(RoleListQueryDto query)
    {
        var queryable = _roleManager.Roles.AsQueryable();

        // 关键词搜索（名称、描述）
        if (!string.IsNullOrEmpty(query.Keyword))
        {
            var keyword = query.Keyword!.ToLower();
            queryable = queryable.Where(r =>
                (r.Name != null && r.Name.ToLower().Contains(keyword)) ||
                (r.Description != null && r.Description.ToLower().Contains(keyword)));
        }

        // 系统角色筛选
        if (query.IsSystem.HasValue)
        {
            queryable = queryable.Where(r => r.IsSystem == query.IsSystem.Value);
        }

        // 默认角色筛选
        if (query.IsDefault.HasValue)
        {
            queryable = queryable.Where(r => r.IsDefault == query.IsDefault.Value);
        }

        // 默认按创建时间降序
        queryable = queryable.OrderByDescending(r => r.CreationTime);

        var paged = await queryable
            .ProjectTo<Role, RoleDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    public async Task<Result<RoleDetailDto>> GetDetailAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDetailDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        var detail = role.MapTo<RoleDetailDto>();

        // 查询角色下的用户数量
        detail.UserCount = await _dbContext.Set<UserRole>()
            .CountAsync(ur => ur.RoleId == id);

        return Ok(detail);
    }

    public async Task<Result<RoleDto>> GetByIdAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> GetByNameAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleDto input)
    {
        // 检查重名
        if (await _roleManager.RoleExistsAsync(input.Name))
        {
            return Fail<RoleDto>($"Role '{input.Name}' already exists", 409, ErrorCodes.IDENTITY_ROLE_ALREADY_EXISTS);
        }

        var role = new Role
        {
            Name = input.Name,
            Description = input.Description,
            IsDefault = input.IsDefault
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            return Fail<RoleDto>(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色创建事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleCreatedEvent
            {
                RoleId = role.Id,
                RoleName = role.Name!,
                Description = role.Description,
                CreationTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role created: {RoleId}, Name: {RoleName}", role.Id, role.Name);
        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleDto input)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 系统角色不允许重命名
        if (role.IsSystem && !string.Equals(role.Name, input.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Fail<RoleDto>("System role cannot be renamed", 403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // Capture rename diagnostic - populated on the published event only
        // when the name *actually* changed (case-insensitive). Authorization
        // subscribes to RoleUpdatedEvent and warns when a renamed role is
        // referenced by Authorization.SuperAdminRoles (config-by-name → DB
        // rename is the canonical "I just locked myself out" footgun).
        var previousName = !string.Equals(role.Name, input.Name, StringComparison.OrdinalIgnoreCase)
            ? role.Name
            : null;

        role.Name = input.Name;
        role.Description = input.Description;

        if (input.IsDefault.HasValue)
        {
            role.IsDefault = input.IsDefault.Value;
        }

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return Fail<RoleDto>(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色更新事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleUpdatedEvent
            {
                RoleId = role.Id,
                RoleName = role.Name!,
                PreviousName = previousName,
                Description = role.Description,
                UpdatedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role updated: {RoleId}, Name: {RoleName}", role.Id, role.Name);
        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 系统角色不允许删除
        if (role.IsSystem)
        {
            return Fail("System role cannot be deleted", 403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        var roleName = role.Name ?? string.Empty;
        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色删除事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleDeletedEvent
            {
                RoleId = id,
                RoleName = roleName,
                DeletedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role deleted: {RoleId}, Name: {RoleName}", id, roleName);
        return Ok();
    }

    public async Task<Result> DeleteManyAsync(IEnumerable<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        var failures = new List<string>();

        foreach (var id in ids)
        {
            var role = await _roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                continue; // 已不存在，跳过
            }

            if (role.IsSystem)
            {
                failures.Add($"Role '{role.Name}' is a system role and cannot be deleted");
                continue;
            }

            var roleName = role.Name ?? string.Empty;
            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                failures.Add($"Failed to delete role '{roleName}': {result.FormatErrors()}");
                continue;
            }

            // 发布角色删除事件
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new RoleDeletedEvent
                {
                    RoleId = id,
                    RoleName = roleName,
                    DeletedTime = DateTime.UtcNow
                }, cancellationToken: default);
            }

            LogInformation("Role deleted: {RoleId}, Name: {RoleName}", id, roleName);
        }

        if (failures.Count > 0)
        {
            return Fail(string.Join("; ", failures), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        return Ok();
    }

    public async Task<Result<bool>> ExistsAsync(string name)
    {
        var exists = await _roleManager.RoleExistsAsync(name);
        return Ok(exists);
    }

    public async Task<Result<IPagedList<UserListItemDto>>> GetUsersInRoleAsync(Guid roleId, PagedQueryDto query)
    {
        // 验证角色存在
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return Fail<IPagedList<UserListItemDto>>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 通过 UserRole 关联表查询角色下的用户
        var userIds = _dbContext.Set<UserRole>()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId);

        var usersQuery = _dbContext.Set<User>()
            .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
            .OrderByDescending(u => u.CreationTime);

        var paged = await usersQuery
            .ProjectTo<User, UserListItemDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    public async Task<Result<int>> GetUserCountAsync(Guid roleId)
    {
        // 验证角色存在
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return Fail<int>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        var count = await _dbContext.Set<UserRole>()
            .CountAsync(ur => ur.RoleId == roleId);

        return Ok(count);
    }

    public async Task<Result<IEnumerable<RoleDto>>> GetDefaultRolesAsync()
    {
        var roles = await _roleManager.Roles
            .Where(r => r.IsDefault)
            .ToListAsync();

        return Ok<IEnumerable<RoleDto>>(roles.MapToList<RoleDto>());
    }

    public async Task<Result> SetDefaultAsync(Guid id, bool isDefault)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        role.IsDefault = isDefault;
        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        LogInformation("Role '{RoleName}' default status set to {IsDefault}", role.Name ?? string.Empty, isDefault);
        return Ok();
    }
}
