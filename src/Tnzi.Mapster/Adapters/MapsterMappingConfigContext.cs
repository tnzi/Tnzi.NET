
namespace Tnzi.Mapster.Adapters;

/// <summary>
/// Mapster 映射配置上下文实现
/// </summary>
internal class MapsterMappingConfigContext : IMappingConfigContext
{
    private readonly TypeAdapterConfig _config;

    public MapsterMappingConfigContext(TypeAdapterConfig config)
    {
        _config = Check.NotNull(config);
    }

    public IMappingConfigBuilder<TSource, TDestination> NewConfig<TSource, TDestination>()
    {
        var builder = _config.NewConfig<TSource, TDestination>();
        return new MapsterMappingConfigBuilder<TSource, TDestination>(builder);
    }
}