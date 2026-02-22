
namespace Tnzi.Mapster;

/// <summary>
/// 对象映射扩展操作
/// </summary>
public static class MapperExtensions
{
    private static IMapper? _mapper;

    /// <summary>
    /// 设置对象映射执行者
    /// 使用 Volatile.Write 确保多线程环境下的内存可见性
    /// </summary>
    /// <param name="mapper">映射执行者</param>
    public static void SetMapper(IMapper mapper)
    {
        Volatile.Write(ref _mapper, Check.NotNull(mapper));
    }

    /// <summary>
    /// 将对象映射为指定类型
    /// </summary>
    /// <typeparam name="TTarget">要映射的目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>目标类型的对象</returns>
    public static TTarget MapTo<TTarget>(this object source)
    {
        CheckMapper();
        return Volatile.Read(ref _mapper)!.Map<TTarget>(source);
    }

    /// <summary>
    /// 将对象映射为指定类型（支持null）
    /// </summary>
    /// <typeparam name="TTarget">要映射的目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>目标类型的对象，如果源对象为null则返回default</returns>
    public static TTarget? MapToNullable<TTarget>(this object? source)
        where TTarget : class
    {
        if (source == null)
            return null;

        CheckMapper();
        return Volatile.Read(ref _mapper)!.Map<TTarget>(source);
    }

    /// <summary>
    /// 使用源类型的对象更新目标类型的对象
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="target">待更新的目标对象</param>
    /// <returns>更新后的目标类型对象</returns>
    public static TTarget MapTo<TSource, TTarget>(this TSource source, TTarget target)
    {
        CheckMapper();
        return Volatile.Read(ref _mapper)!.Map(source, target);
    }

    /// <summary>
    /// 将数据源映射为指定类型的集合
    /// </summary>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">数据源</param>
    /// <returns>目标类型的集合</returns>
    public static List<TTarget> MapToList<TTarget>(this IEnumerable<object>? source)
    {
        if (source == null)
            return new List<TTarget>();

        CheckMapper();
        return Volatile.Read(ref _mapper)!.Map<List<TTarget>>(source);
    }

    /// <summary>
    /// 将 IQueryable 投影为指定类型（支持 EF Core SQL 转换）
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TDestination">目标类型</typeparam>
    /// <param name="source">源查询</param>
    /// <param name="config">映射配置（可选，默认使用全局配置）</param>
    /// <returns>投影后的查询</returns>
    /// <remarks>
    /// 注意：如果映射配置中包含无法转换为 SQL 的表达式（如导航属性的条件表达式），
    /// EF Core 可能会抛出异常。建议在映射配置中避免使用复杂的条件表达式，
    /// 或者使用 IgnoreIf 来处理 null 检查。
    ///
    /// 如果遇到转换问题，可以考虑：
    /// 1. 修改映射配置，避免使用条件表达式，直接映射导航属性（如 src.Artist.Name）
    /// 2. 先加载数据（Include），然后使用 MapToList 进行内存映射
    /// </remarks>
    public static System.Linq.IQueryable<TDestination> ProjectTo<TSource, TDestination>(
        this System.Linq.IQueryable<TSource> source,
        TypeAdapterConfig? config = null)
    {
        config ??= TypeAdapterConfig.GlobalSettings;
        return source.ProjectToType<TDestination>(config);
    }

    /// <summary>
    /// 验证映射执行者是否为空
    /// 使用 Volatile.Read 确保多线程环境下读取到最新值
    /// </summary>
    private static void CheckMapper()
    {
        if (Volatile.Read(ref _mapper) == null)
        {
            throw new InvalidOperationException(
                "Mapper has not been initialized. Please ensure MapsterModule is properly configured.");
        }
    }
}
