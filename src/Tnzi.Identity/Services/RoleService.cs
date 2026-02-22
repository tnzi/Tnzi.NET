namespace Tnzi.Identity.Services;

/// <summary>
/// 角色管理服务实现
/// </summary>
public class RoleService : ApplicationService, IRoleService
{
    private readonly RoleManager<Role> _roleManager;

    public RoleService(RoleManager<Role> roleManager, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _roleManager = Check.NotNull(roleManager);
    }

    public async Task<Result<IEnumerable<RoleDto>>> GetAllAsync()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var roleDtos = roles.MapToList<RoleDto>();
        return Ok<IEnumerable<RoleDto>>(roleDtos);
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
            Description = input.Description
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

        role.Name = input.Name;
        role.Description = input.Description;

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

    public async Task<Result<bool>> ExistsAsync(string name)
    {
        var exists = await _roleManager.RoleExistsAsync(name);
        return Ok(exists);
    }
}
