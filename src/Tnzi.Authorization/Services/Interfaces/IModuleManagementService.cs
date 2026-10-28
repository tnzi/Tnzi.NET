namespace Tnzi.Authorization.Services;

/// <summary>
/// 模块管理服务接口
/// 提供模块和功能的CRUD操作
/// </summary>
public interface IModuleManagementService
{
    /// <summary>
    /// 获取模块树
    /// </summary>
    /// <returns>模块树</returns>
    Task<Result<IEnumerable<FunctionModule>>> GetModuleTreeAsync();

    /// <summary>
    /// 获取模块的功能列表（管理面：含停用与退役行，生效判据由调用方按 <c>IsEnabled</c> / <c>IsRetired</c> 自行渲染）
    /// </summary>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    Task<Result<IEnumerable<ModuleFunction>>> GetModuleFunctionsAsync(Guid moduleId);

    /// <summary>
    /// 根据ID获取模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>模块信息</returns>
    Task<Result<FunctionModule>> GetModuleByIdAsync(Guid id);

    /// <summary>
    /// 获取所有模块
    /// </summary>
    /// <returns>模块列表</returns>
    Task<Result<IEnumerable<FunctionModule>>> GetModulesAsync();

    /// <summary>
    /// 权限目录：全部模块（平铺）连同各自的全部功能，一次读出。管理面口径，含停用与退役行。
    /// </summary>
    /// <remarks>
    /// 角色授权、用户直授与权限目录三张管理页的共同数据源，由各自控制器在自己的类级门下暴露。
    /// 默认实现按模块逐个取功能，且不标 <see cref="PermissionCatalogueModuleDto.IsBuiltIn"/>；
    /// 框架的实现是两条查询并按运行中的模块图标出内置目录。
    /// </remarks>
    async Task<Result<IReadOnlyList<PermissionCatalogueModuleDto>>> GetPermissionCatalogueAsync()
    {
        var modules = await GetModulesAsync();
        if (!modules.Succeeded || modules.Data is null)
        {
            return Result.Failure<IReadOnlyList<PermissionCatalogueModuleDto>>(modules.Message ?? "Failed to read the permission catalogue", modules.Code ?? 500, modules.ErrorCode);
        }

        var catalogue = new List<PermissionCatalogueModuleDto>();
        foreach (var module in modules.Data)
        {
            var functions = await GetModuleFunctionsAsync(module.Id);
            if (!functions.Succeeded || functions.Data is null)
            {
                return Result.Failure<IReadOnlyList<PermissionCatalogueModuleDto>>(functions.Message ?? "Failed to read the permission catalogue", functions.Code ?? 500, functions.ErrorCode);
            }

            catalogue.Add(PermissionCatalogueMapping.ToCatalogueModule(module, functions.Data, isBuiltIn: false));
        }

        return Result.Success<IReadOnlyList<PermissionCatalogueModuleDto>>(catalogue);
    }

    /// <summary>
    /// 创建模块
    /// </summary>
    /// <param name="request">模块信息</param>
    /// <returns>创建的模块</returns>
    Task<Result<FunctionModule>> CreateModuleAsync(CreateFunctionModuleRequest request);

    /// <summary>
    /// 更新模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <param name="request">模块信息</param>
    /// <returns>更新后的模块</returns>
    Task<Result<FunctionModule>> UpdateModuleAsync(Guid id, UpdateFunctionModuleRequest request);

    /// <summary>
    /// 删除模块
    /// </summary>
    /// <param name="id">模块ID</param>
    Task<Result> DeleteModuleAsync(Guid id);

    /// <summary>
    /// 获取模块的子模块
    /// </summary>
    /// <param name="parentId">父模块ID</param>
    /// <returns>子模块列表</returns>
    Task<Result<IEnumerable<FunctionModule>>> GetChildModulesAsync(Guid parentId);

    /// <summary>
    /// 根据ID获取功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>功能信息</returns>
    Task<Result<ModuleFunction>> GetModuleFunctionByIdAsync(Guid id);

    /// <summary>
    /// 创建功能
    /// </summary>
    /// <param name="request">功能信息</param>
    /// <returns>创建的功能</returns>
    Task<Result<ModuleFunction>> CreateModuleFunctionAsync(CreateModuleFunctionRequest request);

    /// <summary>
    /// 更新功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <param name="request">功能信息</param>
    /// <returns>更新后的功能</returns>
    Task<Result<ModuleFunction>> UpdateModuleFunctionAsync(Guid id, UpdateModuleFunctionRequest request);

    /// <summary>
    /// 删除功能
    /// </summary>
    /// <param name="id">功能ID</param>
    Task<Result> DeleteModuleFunctionAsync(Guid id);

    /// <summary>
    /// 启用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    Task<Result> EnableModuleFunctionAsync(Guid id);

    /// <summary>
    /// 禁用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    Task<Result> DisableModuleFunctionAsync(Guid id);

    /// <summary>
    /// 启用模块（同时启用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    Task<Result> EnableModuleAsync(Guid id);

    /// <summary>
    /// 禁用模块（同时禁用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    Task<Result> DisableModuleAsync(Guid id);
}
