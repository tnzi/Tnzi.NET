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
    /// 获取模块的功能列表
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
