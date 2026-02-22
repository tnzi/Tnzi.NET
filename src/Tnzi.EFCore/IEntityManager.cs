namespace Tnzi.EFCore;

/// <summary>
/// 实体管理器接口
/// </summary>
public interface IEntityManager
{
    /// <summary>
    /// 初始化实体类型注册
    /// </summary>
    void Initialize();

    /// <summary>
    /// 获取指定上下文类型的实体配置注册信息
    /// </summary>
    /// <param name="dbContextType">数据上下文类型</param>
    /// <returns>实体注册器数组</returns>
    IEntityRegister[] GetEntityRegisters(Type dbContextType);

    /// <summary>
    /// 获取实体类所属的数据上下文类型
    /// </summary>
    /// <param name="entityType">实体类型</param>
    /// <returns>数据上下文类型</returns>
    Type GetDbContextTypeForEntity(Type entityType);

    /// <summary>
    /// 获取所有已注册的实体类型
    /// </summary>
    /// <returns>实体类型数组</returns>
    Type[] GetAllEntityTypes();

    /// <summary>
    /// 获取所有已注册的 DbContext 类型（不包括占位符类型 typeof(object)）
    /// </summary>
    /// <returns>DbContext 类型数组</returns>
    Type[] GetAllDbContextTypes();
}

