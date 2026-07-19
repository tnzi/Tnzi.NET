namespace Tnzi.Mapping;

/// <summary>
/// 运行时对象映射抽象。
/// 框架级间接层，使核心类型（如 <see cref="Tnzi.Application.CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/>）
/// 能在不引用具体映射库的前提下完成 实体/DTO 映射——核心程序集不依赖 <c>Tnzi.Mapster</c>。
/// Mapster 模块注册一个委托到其静态 <c>MapperExtensions</c> 的实现。
/// </summary>
/// <remarks>
/// 这是对 <c>MapTo</c>/<c>MapToList</c> 扩展方法的接口化封装，语义与之一致：
/// 同名同类型属性自动映射，属性名不一致/导航属性/计算属性由 <see cref="Tnzi.Mapping.IMappingConfig"/> 配置。
/// </remarks>
[StableApi(Since = "0.1.0")]
public interface IObjectMapper
{
    /// <summary>
    /// 将源对象映射为目标类型的新实例。
    /// </summary>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象</param>
    TTarget Map<TTarget>(object source);

    /// <summary>
    /// 使用源对象更新既有目标对象（就地映射，返回同一目标实例）。
    /// 常用于「用 UpdateDto 覆盖已加载实体」。
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="destination">待更新的目标对象</param>
    TTarget Map<TSource, TTarget>(TSource source, TTarget destination);

    /// <summary>
    /// 将源集合映射为目标类型的列表。
    /// </summary>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源集合，<c>null</c> 视为空集合</param>
    List<TTarget> MapToList<TTarget>(IEnumerable<object>? source);
}
