namespace Tnzi.Mapster;

/// <summary>
/// <see cref="IObjectMapper"/> 的 Mapster 实现，委托到静态 <see cref="MapperExtensions"/>。
/// 由 <see cref="MapsterModule"/> 注册为单例。核心类型经 <see cref="IObjectMapper"/> 抽象消费映射能力，
/// 无需引用 <c>Tnzi.Mapster</c>。
/// </summary>
public sealed class MapsterObjectMapper : IObjectMapper
{
    /// <inheritdoc />
    public TTarget Map<TTarget>(object source) => source.MapTo<TTarget>();

    /// <inheritdoc />
    public TTarget Map<TSource, TTarget>(TSource source, TTarget destination)
        => source.MapTo<TSource, TTarget>(destination);

    /// <inheritdoc />
    public List<TTarget> MapToList<TTarget>(IEnumerable<object>? source) => source.MapToList<TTarget>();
}
