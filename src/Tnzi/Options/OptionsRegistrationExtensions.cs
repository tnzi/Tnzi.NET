namespace Tnzi.Options;

/// <summary>Options 统一注册入口：section 单一来源（[ConfigSection] 或类型名推导）+ Bind + 启动期验证。</summary>
public static class OptionsRegistrationExtensions
{
    /// <summary>
    /// 绑定 <typeparamref name="T"/> 并按<b>数据注解</b>做启动期验证。
    /// </summary>
    /// <remarks>
    /// ★ 验证的是 <typeparamref name="T"/> 上的 <c>[Required]</c> / <c>[Range]</c> 之类注解。
    /// 一个既没有注解、也没有 <see cref="IValidateOptions{TOptions}"/> 的类型，
    /// <b>不会被验证任何一项</b> —— 详见带验证器的重载。
    /// </remarks>
    public static OptionsBuilder<T> AddTnziOptions<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class
        => services.AddTnziOptions<T>(configuration, ConfigSectionResolver.Resolve(typeof(T)));

    /// <inheritdoc cref="AddTnziOptions{T}(IServiceCollection, IConfiguration)"/>
    /// <remarks>
    /// <para>
    /// ★★ <b>此前这个重载只调 <c>ValidateOnStart()</c>，那读起来像在验证，实际什么都不验。</b>
    /// <c>ValidateOnStart</c> 做的事只是在启动时把 <c>IOptions&lt;T&gt;.Value</c> 取出来一次，
    /// 从而触发<b>已注册的</b> <see cref="IValidateOptions{TOptions}"/>；一个都没注册时它就是个空操作。
    /// 于是 <c>MaxRecipients: 0</c> 这类值会绑定成功、启动成功、一路进到运行时，全程零症状。
    /// 补上 <c>ValidateDataAnnotations()</c> 之后，写在选项类上的注解才真正生效。
    /// </para>
    /// <para>
    /// 但注解只能表达单字段约束。<b>跨字段规则（A 开启时 B 必填、上下限互相牵制）仍然只有验证器能表达</b>，
    /// 那种情况请用 <see cref="AddTnziOptions{T, TValidator}(IServiceCollection, IConfiguration)"/>，
    /// 验证器继承 <see cref="OptionsValidatorBase{TOptions}"/>。
    /// </para>
    /// <para>
    /// ★ <b>选一个</b>：注解或验证器。两者都没有的选项类，这里依旧一条都验不了 ——
    /// 补上 <c>ValidateDataAnnotations()</c> 只是让「写了注解就生效」成立，
    /// 它变不出没写过的约束。
    /// </para>
    /// </remarks>
    public static OptionsBuilder<T> AddTnziOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        Check.NotNull(services);
        Check.NotNull(configuration);
        Check.NotNullOrWhiteSpace(section);
        var builder = services.AddOptions<T>().Bind(configuration.GetSection(section));
        builder.ValidateDataAnnotations();
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
