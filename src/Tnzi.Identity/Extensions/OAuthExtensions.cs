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
    /// </summary>
    private static void TryAddGitHub(AuthenticationBuilder builder, string clientId, string clientSecret, string callbackPath)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AspNet.Security.OAuth.GitHub");

            if (assembly != null)
            {
                var extensionType = assembly.GetType("AspNet.Security.OAuth.GitHub.GitHubAuthenticationExtensions");
                if (extensionType != null)
                {
                    var addGitHubMethod = extensionType.GetMethods()
                        .FirstOrDefault(m => m.Name == "AddGitHub" &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType == typeof(AuthenticationBuilder));

                    if (addGitHubMethod != null)
                    {
                        var optionsType = assembly.GetType("AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions");
                        if (optionsType != null)
                        {
                            var options = Activator.CreateInstance(optionsType);
                            optionsType.GetProperty("ClientId")?.SetValue(options, clientId);
                            optionsType.GetProperty("ClientSecret")?.SetValue(options, clientSecret);
                            optionsType.GetProperty("SignInScheme")?.SetValue(options, "Identity.External");

                            var callbackPathProp = optionsType.GetProperty("CallbackPath");
                            if (callbackPathProp != null)
                            {
                                var pathValue = Activator.CreateInstance(callbackPathProp.PropertyType, callbackPath);
                                if (pathValue != null)
                                {
                                    callbackPathProp.SetValue(options, pathValue);
                                }
                            }

                            if (options != null)
                            {
                                addGitHubMethod.Invoke(null, new object[] { builder, options });
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // GitHub OAuth 包未安装或调用失败，忽略
        }
    }
}
