namespace Tnzi.Mapping;

/// <summary>
/// 对象映射配置接口
/// 框架级别的抽象，不依赖具体的映射库实现
/// </summary>
public interface IMappingConfig
{
    /// <summary>
    /// 配置映射规则
    /// </summary>
    /// <param name="context">映射配置上下文</param>
    void Configure(IMappingConfigContext context);
}

/// <summary>
/// 映射配置上下文
/// 提供类型安全的映射配置方法
/// </summary>
public interface IMappingConfigContext
{
    /// <summary>
    /// 创建新的映射配置
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TDestination">目标类型</typeparam>
    /// <returns>映射配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> NewConfig<TSource, TDestination>();
}

/// <summary>
/// 映射配置构建器
/// 提供类型安全的映射配置方法
/// </summary>
/// <typeparam name="TSource">源类型</typeparam>
/// <typeparam name="TDestination">目标类型</typeparam>
public interface IMappingConfigBuilder<TSource, TDestination>
{
    /// <summary>
    /// 映射指定属性
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="destinationMember">目标属性表达式</param>
    /// <param name="sourceExpression">源表达式</param>
    /// <returns>配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> Map<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember,
        Expression<Func<TSource, TProperty>> sourceExpression);

    /// <summary>
    /// 映射指定属性，如果源值为 null 则使用默认值
    /// 兼容 ProjectTo（SQL 转换），在内存映射时自动处理默认值
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="destinationMember">目标属性表达式</param>
    /// <param name="sourceExpression">源表达式（可直接使用导航属性，如 src => src.Artist.Name）</param>
    /// <param name="defaultValue">默认值（当源值为 null 时使用）</param>
    /// <returns>配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> Map<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember,
        Expression<Func<TSource, TProperty?>> sourceExpression,
        TProperty defaultValue);

    /// <summary>
    /// 忽略指定属性
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="destinationMember">目标属性表达式</param>
    /// <returns>配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> Ignore<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember);

    /// <summary>
    /// 映射后处理
    /// </summary>
    /// <param name="afterMapping">映射后处理函数</param>
    /// <returns>配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> AfterMapping(
        Action<TSource, TDestination> afterMapping);

    /// <summary>
    /// 映射前处理
    /// </summary>
    /// <param name="beforeMapping">映射前处理函数</param>
    /// <returns>配置构建器</returns>
    IMappingConfigBuilder<TSource, TDestination> BeforeMapping(
        Action<TSource, TDestination> beforeMapping);
}
