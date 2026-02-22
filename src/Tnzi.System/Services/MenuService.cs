namespace Tnzi.System.Services;

/// <summary>
/// 菜单服务实现
/// </summary>
public class MenuService : ApplicationService, IMenuService
{
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly ICache _cache;
    private readonly IFunctionAuthorizationService? _functionAuthorizationService;

    private const string CacheKeyAllMenus = "System:Menus:All";

    public MenuService(
        IServiceProvider serviceProvider,
        IRepository<Menu, Guid> menuRepository,
        ICache cache,
        IFunctionAuthorizationService? functionAuthorizationService = null)
        : base(serviceProvider)
    {
        _menuRepository = Check.NotNull(menuRepository);
        _cache = Check.NotNull(cache);
        _functionAuthorizationService = functionAuthorizationService;
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<MenuDto>>> GetMenusAsync()
    {
        var menus = await _menuRepository.ToListAsync();
        var menuDtos = menus.MapToList<MenuDto>();
        return Ok((IEnumerable<MenuDto>)menuDtos);
    }

    /// <inheritdoc />
    public async Task<Result<MenuDto>> GetMenuByIdAsync(Guid id)
    {
        var menu = await _menuRepository.GetAsync(id);
        if (menu == null)
            return Fail<MenuDto>("Menu not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(menu.MapTo<MenuDto>());
    }

    /// <inheritdoc />
    public async Task<Result<MenuDto>> CreateMenuAsync(CreateMenuDto input)
    {
        Check.NotNull(input);

        var entity = input.MapTo<Menu>();
        await _menuRepository.InsertAsync(entity);

        LogInformation("Menu created: {Name}, Id: {Id}", input.Name, entity.Id);
        await InvalidateMenuCacheAsync();
        return Ok(entity.MapTo<MenuDto>(), "Menu created successfully");
    }

    /// <inheritdoc />
    public async Task<Result<MenuDto>> UpdateMenuAsync(Guid id, UpdateMenuDto input)
    {
        Check.NotNull(input);

        var entity = await _menuRepository.GetAsync(id);
        if (entity == null)
            return Fail<MenuDto>("Menu not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        input.MapTo(entity);
        await _menuRepository.UpdateAsync(entity);

        LogInformation("Menu updated: {Name}, Id: {Id}", input.Name, id);
        await InvalidateMenuCacheAsync();
        return Ok(entity.MapTo<MenuDto>(), "Menu updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteMenuAsync(Guid id)
    {
        var menu = await _menuRepository.GetAsync(id);
        if (menu == null)
            return Fail("Menu not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 检查是否有子菜单
        var hasChildren = await _menuRepository.Where(m => m.ParentId == id).AnyAsync();
        if (hasChildren)
            return Fail("Cannot delete menu with children", 400, ErrorCodes.SYSTEM_MENU_HAS_CHILDREN);

        await _menuRepository.DeleteAsync(id);
        LogInformation("Menu deleted: {Name}, Id: {Id}", menu.Name, id);
        await InvalidateMenuCacheAsync();
        return Ok("Menu deleted successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteMenusAsync(IEnumerable<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        var idList = ids.ToList();

        // 检查是否有子菜单引用了这些菜单（但不在删除列表中）
        var hasChildren = await _menuRepository
            .Where(m => m.ParentId.HasValue && idList.Contains(m.ParentId.Value) && !idList.Contains(m.Id))
            .AnyAsync();

        if (hasChildren)
            return Fail("Cannot delete menus that have children not included in the deletion list", 400, ErrorCodes.SYSTEM_MENU_HAS_CHILDREN);

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var menus = await _menuRepository
                .Where(m => idList.Contains(m.Id))
                .ToListAsync(cancellationToken);

            await _menuRepository.DeleteManyAsync(menus);
        });

        LogInformation("Batch deleted {Count} menus", idList.Count);
        await InvalidateMenuCacheAsync();
        return Ok($"Deleted {idList.Count} menus successfully");
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<MenuTreeNode>>> GetUserMenuTreeAsync(Guid userId)
    {
        // 获取所有菜单（使用缓存）
        var allMenus = await GetAllMenusCachedAsync();

        // 获取用户的功能权限代码（如果功能授权服务可用）
        HashSet<string>? userPermissions = null;
        if (_functionAuthorizationService != null)
        {
            try
            {
                var userPermissionNames = await _functionAuthorizationService.GetUserPermissionNamesAsync(userId);
                userPermissions = new HashSet<string>(userPermissionNames, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to get user permission names from authorization service");
            }
        }

        // 过滤菜单：隐藏的菜单不显示，有权限要求的菜单需要检查权限
        // 权限过滤采用 fail-closed：授权服务不可用时，隐藏有权限要求的菜单
        var menuList = allMenus
            .Where(m => !m.IsHidden)
            .Where(m => string.IsNullOrEmpty(m.Permission) || (userPermissions != null && userPermissions.Contains(m.Permission)))
            .OrderBy(m => m.SortOrder)
            .Select(m => new MenuTreeNode
            {
                Id = m.Id,
                ParentId = m.ParentId,
                Name = m.Name,
                Icon = m.Icon,
                Path = m.Path,
                Component = m.Component,
                SortOrder = m.SortOrder,
                IsHidden = m.IsHidden,
                Permission = m.Permission,
                Type = m.Type
            })
            .ToList();

        // 构建树结构
        var tree = BuildMenuTree(menuList);
        return Ok((IEnumerable<MenuTreeNode>)tree);
    }

    /// <inheritdoc />
    public async Task<Result> UpdateMenuOrdersAsync(IEnumerable<MenuOrderDto> orders)
    {
        Check.NotNullOrEmpty(orders);

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var orderDict = orders.ToDictionary(m => m.Id, m => m.SortOrder);
            var menuIds = orderDict.Keys.ToList();
            var menus = await _menuRepository
                .Where(m => menuIds.Contains(m.Id))
                .ToListAsync(cancellationToken);

            foreach (var menu in menus)
            {
                if (orderDict.TryGetValue(menu.Id, out var sortOrder))
                {
                    menu.SortOrder = sortOrder;
                }
            }

            await _menuRepository.UpdateManyAsync(menus);
        });

        LogInformation("Updated menu orders");
        await InvalidateMenuCacheAsync();
        return Ok("Menu orders updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result> MoveMenuAsync(Guid id, Guid? newParentId)
    {
        var menu = await _menuRepository.GetAsync(id);
        if (menu == null)
            return Fail("Menu not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 不能将菜单移动到自己下面
        if (newParentId == id)
            return Fail("Cannot move menu under itself", 400, ErrorCodes.VALIDATION_ERROR);

        // 如果指定了新父级，验证不会产生循环引用
        if (newParentId.HasValue)
        {
            var parentMenu = await _menuRepository.GetAsync(newParentId.Value);
            if (parentMenu == null)
                return Fail("Target parent menu not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

            // 检查循环引用：遍历新父级的祖先链，确保不包含当前菜单
            if (await HasCircularReferenceAsync(id, newParentId.Value))
                return Fail("Cannot move menu: circular reference detected", 400, ErrorCodes.VALIDATION_ERROR);
        }

        menu.ParentId = newParentId;
        await _menuRepository.UpdateAsync(menu);

        LogInformation("Menu moved: {Name}, Id: {Id}, NewParentId: {NewParentId}", menu.Name, id, newParentId?.ToString() ?? "root");
        await InvalidateMenuCacheAsync();
        return Ok("Menu moved successfully");
    }

    #region 私有方法

    /// <summary>
    /// 检查是否存在循环引用
    /// </summary>
    private async Task<bool> HasCircularReferenceAsync(Guid menuId, Guid targetParentId)
    {
        var currentId = targetParentId;
        var visited = new HashSet<Guid> { menuId };

        while (true)
        {
            if (visited.Contains(currentId))
                return true;

            visited.Add(currentId);

            var parent = await _menuRepository
                .AsQueryable()
                .AsNoTracking()
                .Where(m => m.Id == currentId)
                .Select(m => m.ParentId)
                .FirstOrDefaultAsync();

            if (!parent.HasValue)
                return false;

            currentId = parent.Value;
        }
    }

    private async Task<List<Menu>> GetAllMenusCachedAsync()
    {
        var menus = await _cache.GetAsync<List<Menu>>(CacheKeyAllMenus);
        if (menus != null)
            return menus;

        menus = await _menuRepository.AsQueryable().AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync();
        await _cache.SetAsync(CacheKeyAllMenus, menus, TimeSpan.FromHours(1));
        return menus;
    }

    private async Task InvalidateMenuCacheAsync()
    {
        await _cache.RemoveAsync(CacheKeyAllMenus);
    }

    /// <summary>
    /// 构建菜单树（非递归实现，高性能）
    /// </summary>
    private static List<MenuTreeNode> BuildMenuTree(List<MenuTreeNode> allMenus)
    {
        var menuMap = allMenus.ToDictionary(m => m.Id);
        var rootMenus = new List<MenuTreeNode>();

        foreach (var menu in allMenus)
        {
            if (menu.ParentId.HasValue && menuMap.TryGetValue(menu.ParentId.Value, out var parent))
            {
                parent.Children.Add(menu);
            }
            else
            {
                rootMenus.Add(menu);
            }
        }

        return rootMenus;
    }

    #endregion
}
