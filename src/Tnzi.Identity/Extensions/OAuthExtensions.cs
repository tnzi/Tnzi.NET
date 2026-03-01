namespace Tnzi.Identity.Extensions;

/// <summary>
/// OAuth2扩展方法
/// </summary>
public static class OAuthExtensions
{
    /// <summary>
    /// 添加OAuth2第三方登录支持
    /// </summary>
    /// <param name="builder">认证构建器</param>
    /// <param name="configuration">配置</param>
    /// <param name="environment">环境信息（用于判断是否为开发环境）</param>
    /// <returns>认证构建器</returns>
    public static AuthenticationBuilder AddTnziOAuth(this AuthenticationBuilder builder, IConfiguration configuration, IWebHostEnvironment? environment = null)
    {
        // 获取 API 路径前缀
        var apiPrefix = configuration.GetSection("AspNetCore").Get<AspNetCoreOptions>()?.ApiPathPrefix?.TrimEnd('/') ?? "";

        // 构建回调路径（OAuth 中间件会拦截这个路径）
        // 注意：CallbackPath 是相对于应用根路径的，不包含 host
        string BuildCallbackPath(string provider) => $"{apiPrefix}/auth/oauth/{provider}-callback".Replace("//", "/");

        // 判断是否为开发环境
        var envName = environment?.EnvironmentName ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? "";
        var isDevelopment = environment?.IsDevelopment() ?? envName.Equals("Development", StringComparison.OrdinalIgnoreCase);

        // 从配置中读取 OAuth 配置
        var oauthOptions = configuration.GetSection("Identity:OAuth").Get<OAuthOptions>()
            ?? configuration.GetSection("OAuth").Get<OAuthOptions>();

        // 配置 Cookie 选项的通用方法
        void ConfigureCorrelationCookie(CookieBuilder cookie, string name)
        {
            cookie.Name = $".Tnzi.OAuth.Correlation.{name}";
            cookie.HttpOnly = true;
            cookie.SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None;
            cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            cookie.Path = "/";
        }

        // Google OAuth
        var googleClientId = oauthOptions?.Google?.ClientId
            ?? configuration["Identity:OAuth:Google:ClientId"]
            ?? configuration["OAuth:Google:ClientId"];
        var googleClientSecret = oauthOptions?.Google?.ClientSecret
            ?? configuration["Identity:OAuth:Google:ClientSecret"]
            ?? configuration["OAuth:Google:ClientSecret"];
        if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
        {
            builder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.CallbackPath = BuildCallbackPath("google");
                options.SaveTokens = true;
                options.UsePkce = true;
                ConfigureCorrelationCookie(options.CorrelationCookie, "Google");

                // 关键：设置 SignInScheme 为 Identity.External
                // 这样 OAuth 中间件会将认证结果保存到 Identity.External cookie
                options.SignInScheme = "Identity.External";
            });
        }

        // Microsoft OAuth
        var microsoftClientId = oauthOptions?.Microsoft?.ClientId
            ?? configuration["Identity:OAuth:Microsoft:ClientId"]
            ?? configuration["OAuth:Microsoft:ClientId"];
        var microsoftClientSecret = oauthOptions?.Microsoft?.ClientSecret
            ?? configuration["Identity:OAuth:Microsoft:ClientSecret"]
            ?? configuration["OAuth:Microsoft:ClientSecret"];
        if (!string.IsNullOrEmpty(microsoftClientId) && !string.IsNullOrEmpty(microsoftClientSecret))
        {
            builder.AddMicrosoftAccount(MicrosoftAccountDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = microsoftClientId;
                options.ClientSecret = microsoftClientSecret;
                options.CallbackPath = BuildCallbackPath("microsoft");
                options.SaveTokens = true;
                ConfigureCorrelationCookie(options.CorrelationCookie, "Microsoft");
                options.SignInScheme = "Identity.External";
            });
        }

        // Facebook OAuth
        var facebookClientId = oauthOptions?.Facebook?.ClientId
            ?? configuration["Identity:OAuth:Facebook:ClientId"]
            ?? configuration["OAuth:Facebook:AppId"]
            ?? configuration["OAuth:Facebook:ClientId"];
        var facebookClientSecret = oauthOptions?.Facebook?.ClientSecret
            ?? configuration["Identity:OAuth:Facebook:ClientSecret"]
            ?? configuration["OAuth:Facebook:AppSecret"]
            ?? configuration["OAuth:Facebook:ClientSecret"];
        if (!string.IsNullOrEmpty(facebookClientId) && !string.IsNullOrEmpty(facebookClientSecret))
        {
            builder.AddFacebook(FacebookDefaults.AuthenticationScheme, options =>
            {
                options.AppId = facebookClientId;
                options.AppSecret = facebookClientSecret;
                options.CallbackPath = BuildCallbackPath("facebook");
                options.SaveTokens = true;
                ConfigureCorrelationCookie(options.CorrelationCookie, "Facebook");
                options.SignInScheme = "Identity.External";
            });
        }

        // Twitter OAuth
        var twitterClientId = oauthOptions?.Twitter?.ClientId
            ?? configuration["Identity:OAuth:Twitter:ClientId"]
            ?? configuration["OAuth:Twitter:ConsumerKey"]
            ?? configuration["OAuth:Twitter:ClientId"];
        var twitterClientSecret = oauthOptions?.Twitter?.ClientSecret
            ?? configuration["Identity:OAuth:Twitter:ClientSecret"]
            ?? configuration["OAuth:Twitter:ConsumerSecret"]
            ?? configuration["OAuth:Twitter:ClientSecret"];
        if (!string.IsNullOrEmpty(twitterClientId) && !string.IsNullOrEmpty(twitterClientSecret))
        {
            builder.AddTwitter(TwitterDefaults.AuthenticationScheme, options =>
            {
                options.ConsumerKey = twitterClientId;
                options.ConsumerSecret = twitterClientSecret;
                options.CallbackPath = BuildCallbackPath("twitter");
                options.SaveTokens = true;
                ConfigureCorrelationCookie(options.CorrelationCookie, "Twitter");
                options.SignInScheme = "Identity.External";
            });
        }

        // GitHub OAuth（需要安装 AspNet.Security.OAuth.GitHub 包）
        var githubClientId = oauthOptions?.GitHub?.ClientId
            ?? configuration["Identity:OAuth:GitHub:ClientId"]
            ?? configuration["OAuth:GitHub:ClientId"];
        var githubClientSecret = oauthOptions?.GitHub?.ClientSecret
            ?? configuration["Identity:OAuth:GitHub:ClientSecret"]
            ?? configuration["OAuth:GitHub:ClientSecret"];
        if (!string.IsNullOrEmpty(githubClientId) && !string.IsNullOrEmpty(githubClientSecret))
        {
            TryAddGitHub(builder, githubClientId, githubClientSecret, BuildCallbackPath("github"));
        }

        return builder;
    }

    /// <summary>
    /// 尝试动态添加 GitHub OAuth 支持
    /// 通过反射调用 AspNet.Security.OAuth.GitHub 包的 AddGitHub 扩展方法
    /// </summary>
    private static void TryAddGitHub(AuthenticationBuilder builder, string clientId, string clientSecret, string callbackPath)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AspNet.Security.OAuth.GitHub");

            if (assembly == null)
            {
                return;
            }

            var optionsType = assembly.GetType("AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions");
            var extensionType = assembly.GetType("AspNet.Security.OAuth.GitHub.GitHubAuthenticationExtensions");
            if (optionsType == null || extensionType == null)
            {
                return;
            }

            // AddGitHub 的签名是 (AuthenticationBuilder, Action<GitHubAuthenticationOptions>)
            var actionType = typeof(Action<>).MakeGenericType(optionsType);
            var addGitHubMethod = extensionType.GetMethods()
                .FirstOrDefault(m => m.Name == "AddGitHub"
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == typeof(AuthenticationBuilder)
                    && m.GetParameters()[1].ParameterType == actionType);

            if (addGitHubMethod == null)
            {
                return;
            }

            // 构建 Action<GitHubAuthenticationOptions> 委托
            var configureMethod = typeof(OAuthExtensions).GetMethod(
                nameof(ConfigureGitHubOptions),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (configureMethod == null)
            {
                return;
            }

            // 使用闭包创建配置委托
            var configureDelegate = Delegate.CreateDelegate(actionType,
                null,
                configureMethod.MakeGenericMethod(optionsType));

            // 通过静态字段传递配置参数（反射场景的线程安全由启动阶段单线程保证）
            _pendingGitHubClientId = clientId;
            _pendingGitHubClientSecret = clientSecret;
            _pendingGitHubCallbackPath = callbackPath;

            addGitHubMethod.Invoke(null, [builder, configureDelegate]);
        }
        catch (Exception ex)
        {
            // GitHub OAuth 包未安装或调用失败，记录调试日志
            System.Diagnostics.Debug.WriteLine($"Failed to add GitHub OAuth: {ex.Message}");
        }
    }

    // 启动阶段临时配置参数（单线程安全）
    private static string? _pendingGitHubClientId;
    private static string? _pendingGitHubClientSecret;
    private static string? _pendingGitHubCallbackPath;

    /// <summary>
    /// 通过反射调用的 GitHub OAuth 配置方法
    /// </summary>
    private static void ConfigureGitHubOptions<T>(T options) where T : class
    {
        var optionsType = typeof(T);
        optionsType.GetProperty("ClientId")?.SetValue(options, _pendingGitHubClientId);
        optionsType.GetProperty("ClientSecret")?.SetValue(options, _pendingGitHubClientSecret);
        optionsType.GetProperty("SignInScheme")?.SetValue(options, "Identity.External");
        optionsType.GetProperty("SaveTokens")?.SetValue(options, true);

        var callbackPathProp = optionsType.GetProperty("CallbackPath");
        if (callbackPathProp != null && _pendingGitHubCallbackPath != null)
        {
            var pathValue = Activator.CreateInstance(callbackPathProp.PropertyType, _pendingGitHubCallbackPath);
            if (pathValue != null)
            {
                callbackPathProp.SetValue(options, pathValue);
            }
        }

        // 清理临时参数
        _pendingGitHubClientId = null;
        _pendingGitHubClientSecret = null;
        _pendingGitHubCallbackPath = null;
    }
}
