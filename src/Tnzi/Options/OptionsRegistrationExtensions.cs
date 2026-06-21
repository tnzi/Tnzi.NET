namespace Tnzi.Options;

/// <summary>Options 统一注册入口：section 单一来源（[ConfigSection] 或类型名推导）+ Bind + 启动期验证。</summary>
public static class OptionsRegistrationExtensions
{
    public static OptionsBuilder<T> AddTnziOptions<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class
        => services.AddTnziOptions<T>(configuration, ConfigSectionResolver.Resolve(typeof(T)));

    public static OptionsBuilder<T> AddTnziOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        Check.NotNull(services);
        Check.NotNull(configuration);
        Check.NotNullOrWhiteSpace(section);
        var builder = services.AddOptions<T>().Bind(configuration.GetSection(section));
        builder.ValidateOnStart();
        return builder;
    }

    public static OptionsBuilder<T> AddTnziOptions<T, TValidator>(this IServiceCollection services, IConfiguration configuration)
        where T : class where TValidator : class, IValidateOptions<T>
        => services.AddTnziOptions<T, TValidator>(configuration, ConfigSectionResolver.Resolve(typeof(T)));

    public static OptionsBuilder<T> AddTnziOptions<T, TValidator>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class where TValidator : class, IValidateOptions<T>
    {
        Check.NotNull(services);
        Check.NotNull(configuration);
        Check.NotNullOrWhiteSpace(section);
        return services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateWith<T, TValidator>();
    }
}
