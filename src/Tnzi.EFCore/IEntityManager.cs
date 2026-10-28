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
    /// 按主次获取指定上下文的实体配置注册：主上下文 = 显式绑定它的 ∪ 未指定上下文（<c>DbContextType</c> 为 null）的；
    /// 次上下文 = 只有显式绑定它的。
    /// </summary>
    /// <param name="dbContextType">数据上下文类型</param>
    /// <param name="isPrimary">它是不是主上下文；null 表示判定不了，按 <see cref="GetEntityRegisters(Type)"/> 处理</param>
    /// <returns>实体注册器数组</returns>
    IEntityRegister[] GetEntityRegisters(Type dbContextType, bool? isPrimary) => GetEntityRegisters(dbContextType);

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

