namespace Tnzi.Identity;

/// <summary>
/// 身份认证模块
/// 配置路径：Identity
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[OptionalDependsOn(typeof(Tnzi.Imaging.ImagingModule))]
public class IdentityModule : TnziApplicationModule
{
    /// <summary>
    /// Identity 模块最先加载
    /// </summary>
    public override int LoadOrder => 0;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Identity";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 统一绑定 IdentityOptions（配置路径：Identity）
        context.Services.Configure<IdentityOptions>(configuration.GetSection("Identity"));

        // 单独绑定 SessionOptions（配置路径：Identity:Session）
        context.Services.Configure<SessionOptions>(configuration.GetSection("Identity:Session"));

        // 注册配置验证器
        context.Services.AddSingleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>();
        context.Services.AddSingleton<IValidateOptions<SessionOptions>, SessionOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 自动配置 Identity（如果 DbContext 继承自 IdentityDbContext）
        AutoConfigureIdentity(context.Services, configuration);

        // 注册核心服务（Token）
        context.Services.AddScoped<ITokenService, JwtTokenService>();

        // 注册登录日志和令牌服务
        var loginLogSender = new LoginLogSender();
        context.Services.AddSingleton<ILoginLogSender>(loginLogSender);
        context.Services.AddSingleton<ILoginLogConsumer>(loginLogSender);
        context.Services.AddHostedService<LoginLogBackgroundService>();
        context.Services.AddScoped<LoginLogService>();
        context.Services.AddScoped<ILoginLogService>(sp => sp.GetRequiredService<LoginLogService>());
        context.Services.AddScoped<ILoginLogInternalService>(sp => sp.GetRequiredService<LoginLogService>());
        context.Services.AddScoped<IAuthTokenService, AuthTokenService>();

        // 注册认证服务
        context.Services.AddScoped<IAuthService, AuthService>();

        // 注册注册服务
        context.Services.AddScoped<IRegistrationService, RegistrationService>();

        // 注册密码服务
        context.Services.AddScoped<IPasswordService, PasswordService>();

        // 注册组织架构服务
        context.Services.AddScoped<IOrganizationService, OrganizationService>();
        context.Services.AddScoped<ITenantService, TenantService>();
        context.Services.TryAddScoped<ITenantChecker, TenantChecker>();

        // 注册用户登录记录服务
        context.Services.AddScoped<IUserLoginService, UserLoginService>();

        // 注册用户管理服务
        context.Services.AddScoped<IUserService, UserService>();

        // 注册角色管理服务
        context.Services.AddScoped<IRoleService, RoleService>();

        // 注册用户角色服务（用于授权模块）
        context.Services.AddScoped<IUserRoleService, UserRoleService>();

        // 注册2FA服务
        context.Services.AddScoped<ITwoFactorService, TwoFactorService>();

        // 注册OAuth服务
        context.Services.AddScoped<IOAuthService, OAuthService>();

        // 注册用户详情服务
        context.Services.AddScoped<IUserDetailService, UserDetailService>();

        // 注册密码策略服务
        context.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();

        // 注册会话管理服务（根据配置选择实现）
        RegisterSessionService(context.Services, configuration, context.IsProduction());

        // 注册登录安全服务
        context.Services.AddScoped<ILoginSecurityService, LoginSecurityService>();

        // 注册事件处理器（使用简化方式）
        context.Services.AddEventHandler<UserLoggedInEvent, UserLoggedInEventHandler>();
        context.Services.AddEventHandler<UserLoggedOutEvent, UserLoggedOutEventHandler>();
        context.Services.AddEventHandler<UserLoginFailedEvent, UserLoginFailedEventHandler>();

        // 注册验证码服务
        context.Services.TryAddScoped<ICaptchaService, CaptchaService>();

        // 注册页面生成服务
        context.Services.AddScoped<IIdentityPageService, IdentityPageService>();

        // 配置JWT认证（使用 IOptions<IdentityOptions>）
        // 先配置 JwtBearerOptions，使用 IConfigureOptions 模式在运行时从配置获取值
        // 注意：必须指定认证方案名称，与 AddJwtBearer() 使用的默认方案名一致
        context.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<IdentityOptions>, IWebHostEnvironment>((jwtBearerOptions, identityOptions, environment) =>
            {
                var jwtConfig = identityOptions.Value.Jwt;
                var jwtSecret = jwtConfig.SecretKey;

                // 安全检查：生产环境必须配置 JWT 密钥
                if (string.IsNullOrEmpty(jwtSecret))
                {
                    if (environment.EnvironmentName == "Production")
                    {
                        throw new InvalidOperationException(
                            "JWT SecretKey must be configured in production environment. " +
                            "Please set 'Identity:Jwt:SecretKey' in your configuration.");
                    }
                    // 仅在非生产环境使用默认密钥
                    jwtSecret = "Tnzi_Default_Secret_Key_For_Dev_Only_123456";
                }
                var key = Encoding.UTF8.GetBytes(jwtSecret);

                // 显式声明读写两端依赖的 claim 映射契约 —— 不依赖 MapInboundClaims 的隐式默认。
                // 写端（JwtTokenService）用 ClaimTypes.* 建 claim；入站经 MapInboundClaims 把 JWT
                // 短名映射回长 URI，读端（HttpContextCurrentUser / IsInRole）按长 URI 读 → 三方对齐。
                // 钉死可防迁移到 JsonWebTokenHandler（默认不映射）或他处改配置时静默打破对齐。
                jwtBearerOptions.MapInboundClaims = true;
                jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtConfig.Issuer,
                    ValidAudience = jwtConfig.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                    // 写端固定 HS256（对称密钥）；锁死算法消除算法混淆面，与
                    // JwtTokenService.GetPrincipalFromExpiredToken 的手动 alg 校验一致。
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
                };

                // SignalR 的 WebSocket / ServerSentEvents 传输无法发送 Authorization 头，
                // JS 客户端改用 `access_token` 查询参数携带 JWT。这里把它读入 context.Token，
                // 让这两种传输也能通过 Bearer 校验（LongPolling 走 Authorization 头，本就可用）。
                // Hub 统一挂在 "/hubs" 前缀下（如 Tnzi.Chat 的 "/hubs/chat"）；即便部署在
                // "/api" 之类 PathBase 下，Request.Path 已被剥离为 "/hubs/..."，段匹配各环境一致。
                // 安全：仅对 /hubs 路径读取查询参数中的 token。
                jwtBearerOptions.Events ??= new JwtBearerEvents();
                var previousOnMessageReceived = jwtBearerOptions.Events.OnMessageReceived;
                jwtBearerOptions.Events.OnMessageReceived = async messageContext =>
                {
                    if (previousOnMessageReceived is not null)
                    {
                        await previousOnMessageReceived(messageContext);
                    }

                    if (string.IsNullOrEmpty(messageContext.Token))
                    {
                        var accessToken = messageContext.Request.Query["access_token"];
                        var path = messageContext.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            messageContext.Token = accessToken;
                        }
                    }
                };
            });

        // 然后添加 JWT Bearer 认证
        context.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer()
        .AddTnziOAuth(configuration); // 添加OAuth2第三方登录

        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册会话管理服务
    /// 根据配置选择使用数据库存储或 Redis 分布式存储
    /// </summary>
    private void RegisterSessionService(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        // 读取会话配置
        var sessionOptions = configuration.GetSection("Identity:Session").Get<SessionOptions>() ?? new SessionOptions();

        if (sessionOptions.StorageType == SessionStorageType.Redis)
        {
            // 检查是否已注册 IDistributedCache（Redis/StackExchange 模块会注册）
            // 注意：不检查实现类型，只检查是否注册了 IDistributedCache
            var hasDistributedCache = services.Any(s =>
                s.ServiceType == typeof(IDistributedCache));

            if (hasDistributedCache)
            {
                // 使用 Redis 分布式会话存储
                services.AddScoped<ISessionService, DistributedSessionService>();
            }
            else if (isProduction)
            {
                // 生产环境：立即失败，防止静默配置错误
                throw new ConfigurationException(
                    "Identity:Session:StorageType",
                    "Session storage type is configured as Redis, but IDistributedCache is not registered. " +
                    "Load RedisCachingModule or change StorageType to Database.");
            }
            else
            {
                // 开发环境：降级到数据库存储，记录警告
                services.AddScoped<ISessionService>(provider =>
                {
                    var logger = provider.GetService<ILogger<IdentityModule>>();
                    logger?.LogWarning(
                        "Session storage type is configured as Redis, but IDistributedCache is not registered. " +
                        "Falling back to database storage. To use Redis sessions, add the RedisCachingModule to your dependencies.");

                    var repository = provider.GetRequiredService<IRepository<UserSession, Guid>>();
                    return new DatabaseSessionService(repository, provider);
                });
            }
        }
        else
        {
            // 默认：使用数据库存储（适合简单项目）
            services.AddScoped<ISessionService, DatabaseSessionService>();
        }
    }

    /// <summary>
    /// 自动配置 Identity（如果 DbContext 继承自 IdentityDbContext）
    /// </summary>
    private void AutoConfigureIdentity(IServiceCollection services, IConfiguration configuration)
    {
        // 查找所有已注册的 DbContext
        var dbContextDescriptors = services
            .Where(s => s.ServiceType.IsGenericType && s.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .ToList();

        // IdentityDbContext 的泛型定义类型
        var identityDbContextGenericType = typeof(Data.IdentityDbContext<>);

        foreach (var descriptor in dbContextDescriptors)
        {
            // 获取 DbContext 类型
            var dbContextType = descriptor.ServiceType.GetGenericArguments()[0];

            // 检查是否继承自 IdentityDbContext<TDbContext>
            // 使用类型检查而不是名称检查，更可靠
            var baseType = dbContextType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType)
                {
                    var genericTypeDefinition = baseType.GetGenericTypeDefinition();
                    // 检查是否是 Tnzi.Identity.Data.IdentityDbContext<>
                    if (genericTypeDefinition == identityDbContextGenericType)
                    {
                        // 找到继承自 IdentityDbContext 的 DbContext，自动配置 Identity
                        // 检查是否已注册 Identity
                        var identityBuilderType = typeof(IdentityBuilder);
                        var isAlreadyRegistered = services.Any(s =>
                            s.ServiceType == identityBuilderType ||
                            (s.ServiceType.IsGenericType && s.ServiceType.GetGenericTypeDefinition() == typeof(UserManager<>)));

                        if (!isAlreadyRegistered)
                        {
                            var addIdentityMethod = typeof(IdentityExtensions)
                                .GetMethod(nameof(IdentityExtensions.AddTnziIdentity))!
                                .MakeGenericMethod(dbContextType);

                            addIdentityMethod.Invoke(null, new object[] { services, configuration });
                        }
                        break;
                    }
                }
                baseType = baseType.BaseType;
            }
        }
    }
}

public static class IdentityExtensions
{
    public static IdentityBuilder AddTnziIdentity<TDbContext>(this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        // 从配置中读取 Identity 选项
        var identitySection = configuration.GetSection("Identity");
        var passwordPolicySection = identitySection.GetSection("PasswordPolicy");
        var signInSection = identitySection.GetSection("SignIn");
        var accountSecuritySection = identitySection.GetSection("AccountSecurity");

        return services.AddIdentity<User, Role>(options => {
                // 密码策略
                options.Password.RequireDigit = passwordPolicySection.GetValue("RequireDigit", true);
                options.Password.RequireLowercase = passwordPolicySection.GetValue("RequireLowercase", true);
                options.Password.RequireUppercase = passwordPolicySection.GetValue("RequireUppercase", false);
                options.Password.RequireNonAlphanumeric = passwordPolicySection.GetValue("RequireNonAlphanumeric", false);
                options.Password.RequiredLength = passwordPolicySection.GetValue("MinLength", 6);

                // 用户设置
                options.User.RequireUniqueEmail = signInSection.GetValue("RequireUniqueEmail", true);

                // 锁定设置
                options.Lockout.MaxFailedAccessAttempts = accountSecuritySection.GetValue("MaxFailedLoginAttempts", 5);
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(accountSecuritySection.GetValue("LockoutDurationMinutes", 30));
                options.Lockout.AllowedForNewUsers = accountSecuritySection.GetValue("EnableLockout", true);
            })
            .AddEntityFrameworkStores<TDbContext>()
            .AddDefaultTokenProviders();
    }
}
