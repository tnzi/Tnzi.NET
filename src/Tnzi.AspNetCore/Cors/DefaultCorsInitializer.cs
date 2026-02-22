
namespace Tnzi.AspNetCore.Cors;

/// <summary>
/// 默认 CORS 初始化器
/// </summary>
public class DefaultCorsInitializer : ICorsInitializer
{
    private CorsOptions? _corsOptions;

    private IConfiguration? _configuration;

    /// <summary>
    /// 设置配置（在 AddCors 之前调用）
    /// </summary>
    public void SetConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 添加 CORS 配置
    /// </summary>
    public IServiceCollection AddCors(IServiceCollection services)
    {
        if (_configuration == null)
        {
            // 配置未设置，抛出异常，要求调用方先调用 SetConfiguration
            throw new TnziException(
                "Configuration not set. Call SetConfiguration() before AddCors(). " +
                "This is required to avoid the BuildServiceProvider anti-pattern.");
        }

        var corsOptions = new CorsOptions();
        _configuration.GetSection("AspNetCore:Cors").Bind(corsOptions);
        _corsOptions = corsOptions;

        if (!corsOptions.Enabled)
        {
            return services;
        }

        if (string.IsNullOrEmpty(corsOptions.PolicyName))
        {
            throw new TnziException("The PolicyName of the Tnzi:AspNetCore:Cors node in the configuration file cannot be empty.");
        }

        services.AddCors(opts => opts.AddPolicy(corsOptions.PolicyName, policy =>
        {
            if (corsOptions.AllowAnyHeader)
            {
                policy.AllowAnyHeader();
            }
            else if (corsOptions.WithHeaders != null && corsOptions.WithHeaders.Length > 0)
            {
                policy.WithHeaders(corsOptions.WithHeaders);
            }

            if (corsOptions.AllowAnyMethod)
            {
                policy.AllowAnyMethod();
            }
            else if (corsOptions.WithMethods != null && corsOptions.WithMethods.Length > 0)
            {
                policy.WithMethods(corsOptions.WithMethods);
            }

            if (corsOptions.AllowCredentials)
            {
                policy.AllowCredentials();
            }
            else if (corsOptions.DisallowCredentials)
            {
                policy.DisallowCredentials();
            }

            if (corsOptions.AllowAnyOrigin)
            {
                policy.AllowAnyOrigin();
            }
            else if (corsOptions.WithOrigins != null && corsOptions.WithOrigins.Length > 0)
            {
                policy.WithOrigins(corsOptions.WithOrigins);
            }
        }));

        return services;
    }

    /// <summary>
    /// 应用 CORS 中间件
    /// </summary>
    public IApplicationBuilder UseCors(IApplicationBuilder app)
    {
        if (_corsOptions?.Enabled == true && !string.IsNullOrEmpty(_corsOptions.PolicyName))
        {
            app.UseCors(_corsOptions.PolicyName);
        }

        return app;
    }
}