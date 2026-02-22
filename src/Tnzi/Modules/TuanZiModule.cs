
namespace Tnzi.Modules;

/// <summary>
/// Tnzi 模块抽象基类
/// 提供模块的默认实现，子类只需重写需要的方法
/// 所有生命周期方法均为异步，支持异步初始化场景
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziModuleBase : ITnziModule
{
    /// <summary>
    /// 模块类型（由子类指定）
    /// </summary>
    public abstract ModuleType ModuleType { get; }
    
    /// <summary>
    /// 模块加载顺序（在同类型模块中的顺序，默认0）
    /// 数值越小越先加载
    /// </summary>
    public virtual int LoadOrder => 0;
    
    /// <summary>
    /// 预配置服务（在 ConfigureServices 之前执行）
    /// </summary>
    public virtual Task PreConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
    
    /// <summary>
    /// 配置服务
    /// </summary>
    public virtual Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
    
    /// <summary>
    /// 后配置服务（在所有模块 ConfigureServices 之后执行）
    /// </summary>
    public virtual Task PostConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
    
    /// <summary>
    /// 配置中间件/应用构建
    /// </summary>
    public virtual Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
        => Task.CompletedTask;
    
    /// <summary>
    /// 应用关闭时执行
    /// </summary>
    public virtual Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 核心模块基类（最高优先级）
/// 用于日志、配置等基础服务
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziCoreModule : TnziModuleBase
{
    public override ModuleType ModuleType => ModuleType.Core;
}

/// <summary>
/// 基础设施模块基类
/// 用于缓存、事件总线、ORM等功能模块
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziInfrastructureModule : TnziModuleBase
{
    public override ModuleType ModuleType => ModuleType.Infrastructure;
}

/// <summary>
/// 框架模块基类
/// 用于 ASP.NET Core、SignalR 等框架集成模块
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziFrameworkModule : TnziModuleBase
{
    public override ModuleType ModuleType => ModuleType.Framework;
}

/// <summary>
/// 业务模块基类
/// 用于 Identity、Authorization 等业务逻辑模块
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziApplicationModule : TnziModuleBase, IHasTableNamePrefix
{
    public override ModuleType ModuleType => ModuleType.Application;
    
    /// <summary>
    /// 获取表名前缀（默认无前缀）
    /// 子类可重写此属性以定义模块实体的表名前缀
    /// </summary>
    public virtual string? TableNamePrefix => null;
}

/// <summary>
/// 自定义/扩展模块基类（最低优先级）
/// 用于用户自定义的扩展模块
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class TnziCustomModule : TnziModuleBase
{
    public override ModuleType ModuleType => ModuleType.Custom;
}

/// <summary>
/// 模块工具类
/// </summary>
public static class TnziModuleHelper
{
    /// <summary>
    /// 判断类型是否为 Tnzi 模块
    /// </summary>
    public static bool IsTnziModule(Type type)
    {
        var typeInfo = type.GetTypeInfo();
        return
            typeInfo.IsClass &&
            !typeInfo.IsAbstract &&
            !typeInfo.IsGenericType &&
            typeof(ITnziModule).IsAssignableFrom(type);
    }
    
    /// <summary>
    /// 计算模块的绝对加载顺序
    /// ModuleType 的基础值 + LoadOrder
    /// </summary>
    public static int GetAbsoluteLoadOrder(ITnziModule module)
    {
        return (int)module.ModuleType + module.LoadOrder;
    }
}