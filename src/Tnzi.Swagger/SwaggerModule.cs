
namespace Tnzi.Swagger;

/// <summary>
/// Swagger 模块
/// 配置路径：Swagger
/// 注意：此模块需要在 AspNetCoreModule 之后加载，以确保 AddControllers() 已调用
/// 支持基于 ApiExplorerSettings(GroupName) 的自动分组
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class SwaggerModule : TnziFrameworkModule
{
    /// <summary>
    /// 在 AspNetCore 模块 (LoadOrder: 0) 之后加载
    /// </summary>
    public override int LoadOrder => 10;

    /// <summary>
    /// 缓存 DiscoverGroupNames 结果（不可变集合，线程安全）
    /// 在 ConfigureServicesAsync 中一次性扫描并存储
    /// </summary>
    private FrozenSet<string> _cachedGroupNames = FrozenSet<string>.Empty;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<SwaggerOptions>()
            .Bind(context.Configuration.GetSection("Swagger"))
            .ValidateWith<SwaggerOptions, SwaggerOptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;
        var options = configuration.GetSection("Swagger").Get<SwaggerOptions>() ?? new SwaggerOptions();

        // 如果未启用 Swagger，不注册相关服务
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // AddEndpointsApiExplorer 用于最小 API（Minimal APIs）
        // AddApiExplorer 用于 Controller API（通过 AddControllers 注册）
        // 两者可以共存，Swashbuckle 会自动发现两种类型的 API
        context.Services.AddEndpointsApiExplorer();

        // 确保 DefaultDocument 不为 null（验证器已验证，但为了防御性编程）
        var defaultDoc = options.DefaultDocument ?? throw new InvalidOperationException("Swagger.DefaultDocument cannot be null. Please check your configuration.");

        // 一次性扫描程序集，发现所有自定义分组并缓存为不可变集合
        _cachedGroupNames = DiscoverGroupNames();

        context.Services.AddSwaggerGen(c =>
        {
            // 添加默认文档（用于没有指定 GroupName 的 API）
            c.SwaggerDoc(defaultDoc.Name, new OpenApiInfo
            {
                Title = defaultDoc.Title,
                Version = defaultDoc.Version,
                Description = defaultDoc.Description ?? "APIs without specific group"
            });

            // 添加已发现的自定义分组文档
            foreach (var groupName in _cachedGroupNames)
            {
                if (!string.IsNullOrEmpty(groupName) && groupName != defaultDoc.Name)
                {
                    var title = groupName.ToPascalCase() + " API";
                    c.SwaggerDoc(groupName, new OpenApiInfo
                    {
                        Title = title,
                        Version = defaultDoc.Version,
                        Description = $"APIs for {groupName} group"
                    });
                }
            }

            // 配置 XML 注释文档支持
            if (options.EnableXmlComments)
            {
                var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    c.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
                }
            }

            // 配置文档包含规则：根据 ApiExplorerSettings.GroupName 分配
            c.DocInclusionPredicate((docName, apiDesc) =>
            {
                // 获取方法上的 ApiExplorerSettings
                string? methodGroupName = null;
                if (apiDesc.TryGetMethodInfo(out MethodInfo method))
                {
                    var methodAttr = method.GetCustomAttribute<ApiExplorerSettingsAttribute>();
                    methodGroupName = methodAttr?.GroupName;
                }

                // 获取控制器上的 ApiExplorerSettings
                string? controllerGroupName = null;
                var controllerDescriptor = apiDesc.ActionDescriptor as ControllerActionDescriptor;
                if (controllerDescriptor?.ControllerTypeInfo != null)
                {
                    var controllerAttr = controllerDescriptor.ControllerTypeInfo
                        .GetCustomAttribute<ApiExplorerSettingsAttribute>();
                    controllerGroupName = controllerAttr?.GroupName;
                }

                // 优先使用方法上的 GroupName，其次使用控制器上的
                string? groupName = methodGroupName ?? controllerGroupName;

                // 如果没有指定 GroupName，分配到默认文档
                if (string.IsNullOrEmpty(groupName))
                {
                    return docName == defaultDoc.Name;
                }

                // 根据 GroupName 匹配文档
                return docName == groupName;
            });

            // 添加 JWT 认证支持（如果启用）
            if (options.JwtAuth?.Enabled == true)
            {
                var jwtAuth = options.JwtAuth;
                var description = jwtAuth.Description
                    ?? "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"";

                c.AddSecurityDefinition(jwtAuth.SchemeName, new OpenApiSecurityScheme
                {
                    Description = description,
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = jwtAuth.SchemeName
                });

                // 为所有文档添加安全要求
                c.AddSecurityRequirement(doc =>
                {
                    return new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecuritySchemeReference(jwtAuth.SchemeName),
                            new List<string>()
                        }
                    };
                });
            }

            // 解决相冲突的路由
            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
        });

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 优先使用 WebApp，如果没有则使用 App
        var webApp = context.WebApp;
        var app = webApp ?? context.App;
        if (app == null)
        {
            return Task.CompletedTask;
        }

        // 使用 IOptions 获取已验证的配置
        var options = context.ServiceProvider.GetRequiredService<IOptions<SwaggerOptions>>().Value;

        // 如果未启用 Swagger，不配置中间件
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 确保 DefaultDocument 不为 null
        var defaultDoc = options.DefaultDocument ?? throw new InvalidOperationException("Swagger.DefaultDocument cannot be null. Please check your configuration.");

        // 使用 ConfigureServicesAsync 中缓存的分组（不可变集合，无需重复扫描）
        var allGroups = _cachedGroupNames
            .Where(g => !string.IsNullOrEmpty(g) && g != defaultDoc.Name)
            .OrderBy(g => g)
            .ToList();

        app.UseSwagger();

        if (options.UIProvider == UIProvider.Scalar && webApp != null)
        {
            // 使用 Scalar UI
            webApp.MapScalarApiReference(options.RoutePrefix, o =>
            {
                o.Title = defaultDoc.Title ?? "API Documentation";
            });
        }
        else
        {
            // 使用 Swagger UI
            var swaggerUIOptions = options.SwaggerUI;
            app.UseSwaggerUI(c =>
            {
                // 设置路由前缀（配置 Swagger UI 的访问路径）
                c.RoutePrefix = options.RoutePrefix;

                // 设置默认文档
                // 使用相对路径以支持子目录部署
                var defaultEndpoint = $"{defaultDoc.Name}/swagger.json";
                c.SwaggerEndpoint(defaultEndpoint, defaultDoc.Title);

                // 添加所有分组
                foreach (var groupName in allGroups)
                {
                    var endpoint = $"{groupName}/swagger.json";
                    var title = groupName.ToPascalCase() + " API";
                    c.SwaggerEndpoint(endpoint, title);
                }

                // 应用 Swagger UI 配置
                if (swaggerUIOptions.DisplayRequestDuration)
                {
                    c.DisplayRequestDuration();
                }

                if (swaggerUIOptions.EnableDeepLinking)
                {
                    c.EnableDeepLinking();
                }

                if (swaggerUIOptions.EnableFilter)
                {
                    c.EnableFilter();
                }
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 扫描程序集，发现所有使用 ApiExplorerSettings(GroupName) 的分组
    /// 返回不可变集合，确保线程安全
    /// </summary>
    private static FrozenSet<string> DiscoverGroupNames()
    {
        var groupNames = new HashSet<string>();

        // 扫描所有程序集中的控制器
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                       a.FullName != null &&
                       !a.FullName.StartsWith("System.", StringComparison.Ordinal) &&
                       !a.FullName.StartsWith("Microsoft.", StringComparison.Ordinal) &&
                       !a.FullName.StartsWith("Swashbuckle.", StringComparison.Ordinal));

        foreach (var assembly in assemblies)
        {
            try
            {
                var controllerTypes = assembly.GetTypes()
                    .Where(t =>
                        t.IsClass &&
                        !t.IsAbstract &&
                        (typeof(ControllerBase).IsAssignableFrom(t) ||
                         t.Name.EndsWith("Controller", StringComparison.Ordinal)));

                foreach (var controllerType in controllerTypes)
                {
                    // 检查控制器上的 GroupName
                    var controllerAttr = controllerType.GetCustomAttribute<ApiExplorerSettingsAttribute>();
                    if (controllerAttr != null && !string.IsNullOrEmpty(controllerAttr.GroupName))
                    {
                        groupNames.Add(controllerAttr.GroupName);
                    }

                    // 检查方法上的 GroupName
                    var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.IsPublic && !m.IsSpecialName);

                    foreach (var method in methods)
                    {
                        var methodAttr = method.GetCustomAttribute<ApiExplorerSettingsAttribute>();
                        if (methodAttr != null && !string.IsNullOrEmpty(methodAttr.GroupName))
                        {
                            groupNames.Add(methodAttr.GroupName);
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法加载的程序集
            }
            catch (Exception)
            {
                // 忽略其他异常（如无法访问的程序集）
            }
        }

        return groupNames.ToFrozenSet();
    }
}
