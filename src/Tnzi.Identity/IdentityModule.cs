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
    public override string? TableNamePrefix => IdentityConstants.TablePrefix;

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
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, IdentityPermissions>();

        var configuration = context.Configuration;

        // 自动配置 Identity（如果 DbContext 继承自 IdentityDbContext）
        AutoConfigureIdentity(context.Services, configuration);

        // 注册核心服务（Token）
        context.Services.AddScoped<ITokenService, JwtTokenService>();

        // ★★ AuthToken.Value 以 IDataProtectionProvider 加密落库（刷新令牌、2FA 临时令牌、
        // 设置密码令牌都在那一列上），所以这里必须有 key ring。AddDataProtection 内部走
        // TryAdd，与别的模块重复调用不冲突。
        // ⚠ 运维前提：key ring 必须持久化且多实例共享，否则重启 / 换实例后既有刷新令牌
        // 全部解不开（表现为「所有人被登出一次」）。详见 docs/modules/identity.md。
        context.Services.AddDataProtection();

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

        // 组织架构服务**不在这里注册**：契约 IOrganizationService 留在核心（UserService 与
        // DefaultUserAdminController 以可空可选依赖持有它），实现随可选包
        // Tnzi.Identity.Organization 走。未加载该包时容器里没有实现，那两处按 null 降级。
        context.Services.AddScoped<ITenantService, TenantService>();
        context.Services.TryAddScoped<ITenantChecker, TenantChecker>();

        // 注册用户登录记录服务
        context.Services.AddScoped<IUserLoginService, UserLoginService>();

        // 注册用户管理服务
        context.Services.AddScoped<IUserService, UserService>();

        // 注册角色管理服务
        context.Services.AddScoped<IRoleService, RoleService>();
        // 让不引用 Identity 的模块也能把 CreatorId 显示成名字（可选契约，
        // 未加载 Identity 时根本不注册，消费方按缺失降级）。
        context.Services.AddScoped<IUserDisplayNameProvider, UserDisplayNameProvider>();

        // 注册用户角色服务（用于授权模块）
        context.Services.AddScoped<IUserRoleService, UserRoleService>();

        // 注册2FA服务
        context.Services.AddScoped<ITwoFactorService, TwoFactorService>();

        // 注册 passkey 服务。★ 无条件注册、由 Identity:Passkey:Enabled 在服务层门控 ——
        // 按配置决定要不要注册，会让「配置中心把开关打开」在下次重启前不生效
        // （同 SecurityHeaders / RateLimit 中间件那条热开关判据）。
        context.Services.AddScoped<IPasskeyEnrollmentTokenService, PasskeyEnrollmentTokenService>();

        // 邀请注册。两个接入点的默认实现**在 PostConfigure 阶段**才 TryAdd，见下。
        context.Services.AddScoped<IInvitationService, InvitationService>();
        context.Services.AddScoped<IPasskeyService, PasskeyService>();
        context.Services.AddScoped<IStepUpService, StepUpService>();
        context.Services.AddScoped<IPendingActionService, PendingActionService>();

        // 注册OAuth服务
        context.Services.AddScoped<IOAuthService, OAuthService>();

        // 注册用户详情服务
        context.Services.AddScoped<IUserDetailService, UserDetailService>();

        // 注册密码策略服务
        context.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();

        // 注册会话管理服务（根据配置选择实现）
        RegisterSessionService(context.Services, configuration, context.IsProduction());

        // 注册登录会话协调器（多设备/单设备/限并发策略 + 令牌签发前同步建立会话）
        context.Services.AddScoped<ILoginSessionCoordinator, LoginSessionCoordinator>();

        // ★★ 会话撤销的唯一出口：撤会话 + 删该会话的刷新令牌 + 发事件。
        // 无条件注册 —— 停用账号、改密码、登出、重放检测全都经它，
        // 少了它这些动作会退化成「只撤了会话行、令牌还在」，而那正是本服务存在的理由。
        context.Services.AddScoped<ISessionRevocationService, SessionRevocationService>();

        // 注册登录守卫求值器。始终注册，这样每条令牌签发路径无需判空；
        // 消费应用的守卫（IP 白名单 / 时段 / 设备）另行注册，按 Order 升序排在内置守卫之后。
        context.Services.AddScoped<ILoginGuardEvaluator, LoginGuardEvaluator>();

        // ★★ 内置守卫：账号锁定 / 停用。挂在守卫链上而不是逐条签发路径各查一遍 ——
        // 求值器是全部签发路径的唯一共同调用点，一处实现覆盖全部，
        // 且后续新增的登录方式自动受它保护。详见 LockedAccountLoginGuard 的注释。
        context.Services.AddScoped<ILoginGuard, LockedAccountLoginGuard>();

        // ★★★ 内置守卫：账号已开好但本人还没接受邀请。与上面那条同理同源，
        // 但**不能合并**：邀请状态是一个只有「接受邀请」能改的独立字段，
        // 而账号锁定会被 UserService.EnableAsync 清掉（连同 LockoutEnabled），
        // 那一刻 IsLockedOutAsync 恒为 false，上面那道守卫就不再拦任何东西了。
        // 详见 PendingActionsLoginGuard 的注释。
        context.Services.AddScoped<ILoginGuard, PendingActionsLoginGuard>();

        // 注册会话维护后台服务（定期清理过期/失活会话，避免幽灵会话累积影响并发计数）
        context.Services.AddHostedService<SessionMaintenanceBackgroundService>();

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

        // ★★ 刷新令牌交付过滤器：cookie 模式下把响应体里的刷新令牌搬进 HttpOnly cookie。
        // 全局注册而不是逐个端点调一次 —— 判据是**响应载荷的形状**，与谁产生了这个响应无关，
        // 所以消费方覆写 [DefaultController] 的签发端点也照样受管辖。靠约定要求每个人
        // 记得多调一步，是把一个安全前提押在纪律上（而这一轮已经漏过一次：OAuth 回调）。
        // Bearer 模式（默认）下过滤器第一行就返回，行为与升级前逐字相同。
        context.Services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<RefreshTokenDeliveryFilter>();
        });

        // 配置JWT认证（使用 IOptions<IdentityOptions>）
        // 先配置 JwtBearerOptions，使用 IConfigureOptions 模式在运行时从配置获取值
        // 注意：必须指定认证方案名称，与 AddJwtBearer() 使用的默认方案名一致
        context.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<IdentityOptions>, IWebHostEnvironment>((jwtBearerOptions, identityOptions, environment) =>
            {
                var jwtConfig = identityOptions.Value.Jwt;
                var jwtSecret = jwtConfig.SecretKey;

                // 安全检查：只有开发环境允许回落到内置默认密钥。
                //
                // 判据刻意是「不是 Development」而不是「是 Production」，两处理由：
                // ① `EnvironmentName == "Production"` 是**大小写敏感**的字符串比较，
                //    `ASPNETCORE_ENVIRONMENT=production` 就绕过去了。`IsDevelopment()`
                //    走 OrdinalIgnoreCase，框架其余 8 处判断环境用的都是它。
                // ② 只拦 Production 不够。Staging 通常也是真实部署、连真实数据，
                //    自定义环境名（Prod / Live / prod-eu）更是一个都拦不住。
                //    拒绝列表只挡住想得到的那个，允许列表挡住所有没想到的。
                if (string.IsNullOrEmpty(jwtSecret))
                {
                    if (!environment.IsDevelopment())
                    {
                        throw new InvalidOperationException(
                            $"JWT SecretKey must be configured outside the Development environment "
                            + $"(current: '{environment.EnvironmentName}'). "
                            + "Set 'Identity:Jwt:SecretKey' in your configuration. "
                            + "The built-in default key is public knowledge and must never sign real tokens.");
                    }
                    // 仅开发环境使用默认密钥
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

                // 会话强制校验：令牌携带 session_id 时，每请求校验会话仍有效（未撤销/未过期）。
                // 这是"多设备登录/单设备/限并发"真正生效的关键 —— 会话被撤销后，被踢设备的
                // 下一次请求即 401。无 session_id 的遗留令牌放行（向后兼容）；开关默认开，可经
                // Identity:Session:EnforceSessionValidation 关闭退回旧行为。
                var previousOnTokenValidated = jwtBearerOptions.Events.OnTokenValidated;
                jwtBearerOptions.Events.OnTokenValidated = async tokenContext =>
                {
                    if (previousOnTokenValidated is not null)
                    {
                        await previousOnTokenValidated(tokenContext);
                    }

                    var services = tokenContext.HttpContext.RequestServices;
                    var sessionOptions = services.GetService<IOptions<SessionOptions>>()?.Value;
                    if (sessionOptions is null || !sessionOptions.EnforceSessionValidation)
                    {
                        return;
                    }

                    var sidClaim = tokenContext.Principal?.FindFirst(IdentityConstants.ClaimTypeNames.SessionId)?.Value;
                    if (string.IsNullOrEmpty(sidClaim)
                        || !Guid.TryParse(sidClaim, out var sessionId)
                        || sessionId == Guid.Empty)
                    {
                        return;
                    }

                    var sessionService = services.GetService<ISessionService>();
                    if (sessionService is null)
                    {
                        return;
                    }

                    // ★ 每请求校验的同时比对客户端特征。令牌是无记名凭证 —— 被搬到另一台机器上
                    // 照样能用，而在此之前服务端没有任何一处会察觉：会话行里的 UserAgent
                    // 从建立那天起就没有被读出来比对过。这里是唯一每请求都会经过的位置。
                    var request = tokenContext.HttpContext.Request;
                    var validationContext = new SessionValidationContext(
                        request.Headers.UserAgent.ToString(),
                        request.GetClientIp());

                    var validation = await sessionService.ValidateAsync(sessionId, validationContext);
                    if (validation == SessionValidationResult.Valid)
                    {
                        return;
                    }

                    if (validation == SessionValidationResult.BindingMismatch)
                    {
                        // OWASP《Cookie Theft Mitigation》给的动作：尽快发现被盗用，
                        // 作废会话并要求重新认证。撤销而不是只拒绝这一次请求 ——
                        // 只拒绝的话，攻击者换个 UA 再试一次就进来了。
                        var revocation = services.GetService<ISessionRevocationService>();
                        if (revocation != null)
                        {
                            await revocation.RevokeSessionAsync(sessionId, SessionRevocationReason.BindingMismatch);
                        }

                        tokenContext.Fail("Session binding mismatch");
                        return;
                    }

                    tokenContext.Fail("Session has been revoked or expired");
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
    /// 注册那些「消费应用没提供时才用框架默认」的接入点。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>必须在 PostConfigure 阶段，不能在 Configure 阶段。</strong>
    /// 本模块的 <see cref="LoadOrder"/> 是 0（最先加载），如果在 Configure 阶段
    /// <c>TryAdd</c> 这些默认实现，那么它<b>永远先到</b> —— 消费应用在自己的模块里
    /// （LoadOrder 更大）注册的实现会被 <c>TryAdd</c> 语义挡在门外，
    /// 而<b>调用照样成功</b>：邀请流程正常跑，只是永远跑的是框架那份默认逻辑，
    /// 应用要求的字段和二次验证一个都没生效，且没有任何报错。
    /// 放在 PostConfigure 则相反：所有模块的 Configure 都跑完了，
    /// 此时还没人注册才轮到框架兜底。
    /// </remarks>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        Check.NotNull(context);

        context.Services.TryAddScoped<IInvitationAcceptanceHandler, DefaultInvitationAcceptanceHandler>();
        context.Services.TryAddScoped<IInvitationUrlGenerator, DefaultInvitationUrlGenerator>();

        // 第三方邮箱是否算「已验证」的判定。默认实现保守（只认 Google 的 email_verified
        // 与 Microsoft），消费应用把自己接的提供商摸清楚之后注册自己的实现覆盖它。
        // ★ 同上，必须在 PostConfigure 阶段 TryAdd —— 本模块 LoadOrder = 0，
        // 在 Configure 阶段注册会让消费方的实现被静默挡在门外，而调用照样成功。
        context.Services.TryAddScoped<IOAuthEmailVerificationPolicy, DefaultOAuthEmailVerificationPolicy>();

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
            .AddTnziPasskeyOptions(configuration)
            .AddEntityFrameworkStores<TDbContext>()
            .AddDefaultTokenProviders();
    }

    /// <summary>
    /// 把 <c>Identity:Passkey</c> 里与 WebAuthn 协议相关的部分灌进运行时的
    /// <see cref="IdentityPasskeyOptions"/>。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="IdentityPasskeyOptions"/> 是<strong>独立注册的 options</strong>，
    /// 不是 <c>IdentityOptions</c> 的子对象（跟 Password / Lockout 那些不一样），所以要单独 Configure。
    /// 框架自己的 <c>PasskeyOptions</c> 只管"开不开、挑战活多久、注册令牌活多久"这些框架侧的事。
    /// </remarks>
    private static IdentityBuilder AddTnziPasskeyOptions(this IdentityBuilder builder, IConfiguration configuration)
    {
        var passkeySection = configuration.GetSection("Identity").GetSection("Passkey");

        builder.Services.Configure<IdentityPasskeyOptions>(options =>
        {
            // 留空时不赋值：让运行时按当前请求的 host 推断（本地开发方便）。
            // ★ 生产应显式配置，且改它会让已注册的全部凭据失效 —— 凭据在创建时就绑定了 RP ID。
            var serverDomain = passkeySection.GetValue<string?>("ServerDomain");
            if (!string.IsNullOrWhiteSpace(serverDomain))
            {
                options.ServerDomain = serverDomain;
            }

            // 默认要求用户验证（指纹 / 面容 / PIN）。关掉它，捡到解锁状态设备的人就能登录，
            // passkey 从"双因子"退化成"单因子"。
            options.UserVerificationRequirement =
                passkeySection.GetValue("RequireUserVerification", true) ? "required" : "preferred";

            // ★ 同一个 ChallengeTimeoutSeconds 要同时喂给两侧。浏览器那侧（AuthenticatorTimeout）
            // 决定系统弹窗等多久，服务端那侧决定缓存里的挑战状态活多久（PasskeyService.StoreStateAsync）。
            // 只设一侧，用户会遇到"弹窗还开着、提交回来却说挑战已过期"这类查不出原因的失败。
            var challengeTimeout = passkeySection.GetValue("ChallengeTimeoutSeconds", 300);
            options.AuthenticatorTimeout = TimeSpan.FromSeconds(Math.Max(30, challengeTimeout));
        });

        return builder;
    }
}
