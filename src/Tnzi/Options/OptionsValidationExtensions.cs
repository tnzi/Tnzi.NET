
namespace Tnzi.Options;

/// <summary>
/// Options 验证扩展方法
/// 提供统一的配置验证机制，性能优化：验证只在启动时执行一次，不影响运行时性能
/// </summary>
public static class OptionsValidationExtensions
{
    /// <summary>
    /// 启用启动时验证（推荐使用）
    /// 验证只在 BuildServiceProvider 时执行一次，不影响运行时性能
    /// </summary>
    /// <typeparam name="TOptions">配置选项类型</typeparam>
    /// <param name="builder">Options 构建器</param>
    /// <returns>Options 构建器</returns>
    public static OptionsBuilder<TOptions> ValidateOnStart<TOptions>(
        this OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        // 调用 ASP.NET Core 内置的 ValidateOnStart 扩展方法
        // 使用完全限定名避免递归调用
        OptionsBuilderExtensions.ValidateOnStart(builder);
        return builder;
    }

    /// <summary>
    /// 使用自定义验证器进行启动时验证
    /// </summary>
    /// <typeparam name="TOptions">配置选项类型</typeparam>
    /// <typeparam name="TValidator">验证器类型</typeparam>
    /// <param name="builder">Options 构建器</param>
    /// <returns>Options 构建器</returns>
    public static OptionsBuilder<TOptions> ValidateWith<TOptions, TValidator>(
        this OptionsBuilder<TOptions> builder)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
        // 调用 ASP.NET Core 内置的 ValidateOnStart 扩展方法
        OptionsBuilderExtensions.ValidateOnStart(builder);
        return builder;
    }

    /// <summary>
    /// 使用数据注解验证（轻量级，性能最优）
    /// </summary>
    /// <typeparam name="TOptions">配置选项类型</typeparam>
    /// <param name="builder">Options 构建器</param>
    /// <returns>Options 构建器</returns>
    public static OptionsBuilder<TOptions> ValidateDataAnnotations<TOptions>(
        this OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(
            new DataAnnotationValidateOptions<TOptions>(builder.Name));
        // 调用 ASP.NET Core 内置的 ValidateOnStart 扩展方法
        OptionsBuilderExtensions.ValidateOnStart(builder);
        return builder;
    }
}