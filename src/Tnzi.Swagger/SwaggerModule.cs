
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
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<SwaggerOptions, SwaggerOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = context.Configuration.GetSection("Swagger").Get<SwaggerOptions>() ?? new SwaggerOptions();

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        _ = options.DefaultDocument
            ?? throw new InvalidOperationException("Swagger.DefaultDocument cannot be null. Please check your configuration.");

        context.Services.AddEndpointsApiExplorer();
        context.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerGenOptions>();
        context.Services.AddSwaggerGen();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var webApp = context.WebApp;
        var app = webApp ?? context.App;
        if (app == null)
        {
            return Task.CompletedTask;
        }

        var options = context.ServiceProvider.GetRequiredService<IOptions<SwaggerOptions>>().Value;
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        var defaultDoc = options.DefaultDocument
            ?? throw new InvalidOperationException("Swagger.DefaultDocument cannot be null. Please check your configuration.");
        var apiDescriptionProvider = context.ServiceProvider.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var allGroups = DiscoverGroupNames(apiDescriptionProvider, defaultDoc.Name);

        // 文档 JSON 由 Swashbuckle 的默认路由出（同一路由也出 YAML），与 UI 的 RoutePrefix 无关
        app.UseSwagger();

        var documents = new List<(string Name, string Title)> { (defaultDoc.Name, defaultDoc.Title) };
        documents.AddRange(allGroups.Select(groupName => (groupName, BuildGroupDocumentTitle(groupName))));

        if (options.UIProvider == UIProvider.Scalar && webApp != null)
        {
            webApp.MapScalarApiReference(options.RoutePrefix, o =>
            {
                o.Title = defaultDoc.Title;
                // Scalar 默认去取 /openapi/{documentName}.json（Microsoft.AspNetCore.OpenApi 的路由），
                // 本模块不出那个路由；相对地址由 Scalar 按站点根 + PathBase 解析，与 UI 挂在哪个前缀下无关
                o.WithOpenApiRoutePattern(DocumentJsonRouteTemplate);
                foreach (var (name, title) in documents)
                {
                    o.AddDocument(name, title, isDefault: name == defaultDoc.Name);
                }
            });
        }
        else
        {
            var swaggerUIOptions = options.SwaggerUI;
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = options.RoutePrefix;

                foreach (var (name, title) in documents)
                {
                    c.SwaggerEndpoint(BuildSwaggerUiDocumentUrl(options.RoutePrefix, name), title);
                }

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
    /// 文档 JSON 的路由：Swashbuckle 默认路由 <c>swagger/{documentName}/swagger.{json|yaml}</c> 的 JSON 形态
    /// </summary>
    internal const string DocumentJsonRouteTemplate = "swagger/{documentName}/swagger.json";

    internal static string BuildGroupDocumentTitle(string groupName) => groupName.ToPascalCase() + " API";

    /// <summary>
    /// Swagger UI 页面（<c>{RoutePrefix}/index.html</c>）指向文档 JSON 的相对地址。
    /// 相对地址才能在 PathBase 下照常工作；但它是相对 UI 页面解析的，
    /// 而文档 JSON 的路由不随 RoutePrefix 变，所以前缀不是 <c>swagger</c> 时要先退出前缀目录
    /// </summary>
    internal static string BuildSwaggerUiDocumentUrl(string routePrefix, string documentName)
    {
        var pageDirectory = routePrefix.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var target = DocumentJsonRouteTemplate.Replace("{documentName}", documentName, StringComparison.Ordinal).Split('/');

        var shared = 0;
        while (shared < pageDirectory.Length && shared < target.Length - 1 &&
               string.Equals(pageDirectory[shared], target[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return string.Concat(Enumerable.Repeat("../", pageDirectory.Length - shared)) + string.Join('/', target.Skip(shared));
    }

    internal static FrozenSet<string> DiscoverGroupNames(
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
        string defaultDocName)
    {
        return apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Select(api => api.GroupName)
            .Where(groupName => !string.IsNullOrWhiteSpace(groupName) &&
                                !string.Equals(groupName, defaultDocName, StringComparison.OrdinalIgnoreCase))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(groupName => groupName, StringComparer.OrdinalIgnoreCase)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 扫描已加载的应用程序集，收集"同一简单名出现在多个命名空间"的类型名集合。
    /// 确定性（与文档生成顺序无关），一次扫描后冻结
    /// </summary>
    public static FrozenSet<string> BuildAmbiguousTypeNameSet()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null ||
                assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                assemblyName is "mscorlib" or "netstandard")
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.OfType<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (seen.TryGetValue(type.Name, out var existingNamespace))
                {
                    if (!string.Equals(existingNamespace, type.Namespace, StringComparison.Ordinal))
                    {
                        ambiguous.Add(type.Name);
                    }
                }
                else
                {
                    seen[type.Name] = type.Namespace ?? string.Empty;
                }
            }
        }

        return ambiguous.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 生成 schemaId：泛型按"参数在前 + 去元数名"拼接（与既有默认形态一致），
    /// 歧义简单名追加命名空间限定前缀（去 Tnzi/Dtos/Models 噪音段）
    /// </summary>
    public static string BuildSchemaId(Type type, IReadOnlySet<string> ambiguousNames)
    {
        if (type.IsGenericType)
        {
            var prefix = string.Concat(type.GetGenericArguments().Select(a => BuildSchemaId(a, ambiguousNames)));
            return prefix + Qualify(type, StripArity(type.Name), ambiguousNames);
        }

        return Qualify(type, StripArity(type.Name), ambiguousNames);
    }

    private static string StripArity(string name)
    {
        var index = name.IndexOf('`');
        return index > 0 ? name[..index] : name;
    }

    private static string Qualify(Type type, string simpleName, IReadOnlySet<string> ambiguousNames)
    {
        if (!ambiguousNames.Contains(type.Name))
        {
            return simpleName;
        }

        // Finance.Dtos.InvoiceDto → FinanceInvoiceDto；MyApp.Blog.Dtos.PostDto → MyAppBlogPostDto
        var tokens = (type.Namespace ?? string.Empty).Split('.')
            .Where(t => t.Length > 0 && t is not ("Tnzi" or "Dtos" or "Models" or "Contracts" or "Options"));
        return string.Concat(tokens) + simpleName;
    }
}

internal sealed class ConfigureSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly SwaggerOptions _options;
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionProvider;

    public ConfigureSwaggerGenOptions(
        IOptions<SwaggerOptions> options,
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider)
    {
        _options = options.Value;
        _apiDescriptionProvider = apiDescriptionProvider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        var defaultDoc = _options.DefaultDocument
            ?? throw new InvalidOperationException("Swagger.DefaultDocument cannot be null. Please check your configuration.");

        options.SwaggerDoc(defaultDoc.Name, new OpenApiInfo
        {
            Title = defaultDoc.Title,
            Version = defaultDoc.Version,
            Description = defaultDoc.Description ?? "APIs without specific group"
        });

        foreach (var groupName in SwaggerModule.DiscoverGroupNames(_apiDescriptionProvider, defaultDoc.Name))
        {
            options.SwaggerDoc(groupName, new OpenApiInfo
            {
                Title = SwaggerModule.BuildGroupDocumentTitle(groupName),
                Version = defaultDoc.Version,
                Description = $"APIs for {groupName} group"
            });
        }

        if (_options.EnableXmlComments)
        {
            var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
            foreach (var xmlFile in xmlFiles)
            {
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
            }
        }

        // 跨模块同名 DTO（如 Tnzi.Payment.Dtos.InvoiceDto 与 Tnzi.Finance.Dtos.InvoiceDto）在
        // Swashbuckle 默认短名 schemaId 下冲突——任一分组文档同时引用两者即整组 500。
        // 仅对真正歧义的简单名启用命名空间限定 ID（FinanceInvoiceDto / PaymentInvoiceDto），
        // 其余类型保持默认短名，避免 OpenAPI codegen 客户端全量改名。
        var ambiguousNames = SwaggerModule.BuildAmbiguousTypeNameSet();
        options.CustomSchemaIds(type => SwaggerModule.BuildSchemaId(type, ambiguousNames));

        options.DocInclusionPredicate((docName, apiDesc) =>
        {
            string? methodGroupName = null;
            if (apiDesc.TryGetMethodInfo(out MethodInfo method))
            {
                var methodAttr = method.GetCustomAttribute<ApiExplorerSettingsAttribute>();
                methodGroupName = methodAttr?.GroupName;
            }

            string? controllerGroupName = null;
            if (apiDesc.ActionDescriptor is ControllerActionDescriptor controllerDescriptor &&
                controllerDescriptor.ControllerTypeInfo != null)
            {
                var controllerAttr = controllerDescriptor.ControllerTypeInfo
                    .GetCustomAttribute<ApiExplorerSettingsAttribute>();
                controllerGroupName = controllerAttr?.GroupName;
            }

            var groupName = methodGroupName ?? controllerGroupName;
            if (string.IsNullOrEmpty(groupName))
            {
                return docName == defaultDoc.Name;
            }

            return docName == groupName;
        });

        if (_options.JwtAuth?.Enabled == true)
        {
            var jwtAuth = _options.JwtAuth;
            var description = jwtAuth.Description
                ?? "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"";

            options.AddSecurityDefinition(jwtAuth.SchemeName, new OpenApiSecurityScheme
            {
                Description = description,
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = jwtAuth.SchemeName
            });

            // 引用必须带上宿主文档：Microsoft.OpenApi 2.x 序列化时跳过解析不到的方案引用，
            // 不带就输出 "security": [{}]（不需要任何凭据），Swagger UI 点了 Authorize 也不发 Authorization 头
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(jwtAuth.SchemeName, doc),
                    new List<string>()
                }
            });
        }

        options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
    }
}
