namespace Tnzi.System.Configuration;

/// <summary>
/// IConfigurationBuilder 扩展：注册 SettingConfigurationSource 让 Setting 表能
/// 参与到 ASP.NET Core 标准 IConfiguration / IOptionsMonitor 体系。
/// </summary>
[StableApi(Since = "0.1.0")]
public static class SettingConfigurationBuilderExtensions
{
    internal const string PropertiesKey = "Tnzi.System.Configuration.SettingConfigurationSource";

    /// <summary>
    /// 把 SettingConfigurationSource 加到 IConfigurationBuilder。
    /// 同一个 builder 多次调用幂等（返回已有的 source 实例）。
    /// SystemModule 在 PreConfigureServicesAsync 中自动调用本方法（宿主配置为 ConfigurationManager 时，
    /// TnziApp 流程即是），因此通常无需手动接线；手动调用仅在需要 <paramref name="excludedKeys"/>
    /// 或宿主传入非 builder 型 IConfiguration 时才有必要。
    /// 配置 System:Settings:EnableConfigurationSource=false 可关闭自动接线。
    /// </summary>
    /// <param name="builder">IConfigurationBuilder 实例。</param>
    /// <param name="excludedKeys">
    /// 永不进 IConfiguration 的 key 列表（OrdinalIgnoreCase）。
    /// 适合保护启动级关键开关（如测试模式标志），即使写入 Setting 表也不会被业务代码读到。
    /// </param>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Configuration.AddTnziSettings("MyModule:IsTestMode", "MyModule:TestRedirectEmail");
    /// await TnziApp.RunAsync&lt;StartupModule&gt;(builder);
    /// </code>
    /// </example>
    public static IConfigurationBuilder AddTnziSettings(this IConfigurationBuilder builder, params string[] excludedKeys)
    {
        Check.NotNull(builder);

        if (builder.Properties.TryGetValue(PropertiesKey, out var existing) && existing is SettingConfigurationSource)
            return builder;

        var source = new SettingConfigurationSource
        {
            ExcludedKeys = new HashSet<string>(excludedKeys ?? [], StringComparer.OrdinalIgnoreCase)
        };
        builder.Properties[PropertiesKey] = source;
        builder.Add(source);
        return builder;
    }

    /// <summary>
    /// 取出之前通过 <see cref="AddTnziSettings"/> 注册到 builder 的 source 实例。
    /// SystemModule.ConfigureServicesAsync 在 ServiceConfigurationContext.Configuration (IConfigurationRoot)
    /// 上调用本方法，把 source 注册成 DI singleton 让 OnApplicationInitializationAsync 拿到。
    /// </summary>
    public static SettingConfigurationSource? GetTnziSettingsSource(this IConfiguration configuration)
    {
        if (configuration is not IConfigurationRoot root)
            return null;

        foreach (var provider in root.Providers)
        {
            if (provider is SettingConfigurationProvider settingProvider)
                return settingProvider.OwningSource;
        }

        return null;
    }
}
