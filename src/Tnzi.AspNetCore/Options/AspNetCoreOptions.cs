namespace Tnzi.AspNetCore.Options;

/// <summary>
/// ASP.NET Core 模块配置选项
/// 配置路径：AspNetCore
/// </summary>
public class AspNetCoreOptions
{
    /// <summary>
    /// 获取或设置 是否采集客户端来源地址。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 置为 <c>false</c> 后，<c>GetClientIp()</c> 一律返回 <c>null</c>，
    /// 于是请求日志、访问日志、审计上下文、限流告警等<strong>全部下游一次性拿不到地址</strong>——
    /// 这是唯一的采集入口，在这里切断比给每个消费者各加一个开关可靠得多。
    /// </para>
    /// <para>
    /// <strong>与「脱敏」的区别。</strong>脱敏是把已经取到的值替换掉，它是一行可以被误删的代码；
    /// 这个开关让值<em>从来没有被取到过</em>。对匿名举报、举报人保护一类的场景，
    /// 「日志里没有这个字段」和「日志里这个字段是星号」是两件事：前者在被强制要求交出日志时也无从交出。
    /// </para>
    /// <para>
    /// <strong>关闭它会影响限流。</strong>匿名请求的限流分区默认就是按来源地址做的，
    /// 关掉之后匿名请求将没有分区键，行为由 <see cref="RateLimitOptions.MissingPartitionKey"/> 决定。
    /// 需要同时保住限流的部署，应注册自己的 <c>IRateLimitPartitionKeyProvider</c>
    /// （例如按一次性提交票据分区），或把该选项设为拒绝。
    /// </para>
    /// <para>
    /// <strong>刻意不做成运行时可改的设置项。</strong>它是部署级的隐私决策：
    /// 若能从管理端热改，等于给「悄悄把地址采集打开」留了一扇不留痕迹的门。
    /// </para>
    /// </remarks>
    public bool CollectClientIpAddress { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否启用全局模型验证过滤器
    /// </summary>
    public bool EnableGlobalModelValidation { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否启用全局工作单元过滤器
    /// 默认值：false（可选标记模式，用户通过 [UnitOfWork] 特性标记需要事务的方法）
    /// 当为 true 时，所有 Action 自动应用事务（可以通过 [UnitOfWork(IsDisabled = true)] 禁用特定方法）
    /// </summary>
    public bool EnableGlobalUnitOfWork { get; set; } = false;

    /// <summary>
    /// 获取或设置 是否启用 SPA 404 处理中间件
    /// </summary>
    public bool EnableSPANotFoundHandler { get; set; } = false;

    /// <summary>
    /// 获取或设置 是否把框架欢迎页映射到根路径 <c>/</c>。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 自带前端的宿主要把这项设为 <c>false</c>，否则 <c>GET /</c> 拿到的是框架欢迎页而不是
    /// <c>wwwroot/index.html</c>。
    /// </para>
    /// <para>
    /// <strong>为什么只有根路径受影响。</strong>欢迎页是一个<em>已匹配的端点</em>，
    /// 而 <c>UseDefaultFiles()</c> / <c>UseStaticFiles()</c> 在
    /// <c>HttpContext.GetEndpoint()</c> 非空时会主动让路（避免与端点抢同一个请求）。
    /// 于是静态文件管线恰好在 <c>/</c> 上、也只在 <c>/</c> 上被跳过：
    /// <c>/index.html</c> 与各级深链都匹配不到端点，照常由静态文件或
    /// <see cref="EnableSPANotFoundHandler"/> 处理。这正是「整站都好，唯独首页是框架页」的成因，
    /// 而它读起来像「站点起来了」不像故障。
    /// </para>
    /// <para>
    /// <strong>刻意做成显式开关而不是自动让路。</strong>「web root 里有 index.html 就自动不映射」
    /// 看似更省事，但框架在映射路由那一刻无从判断：默认文件名可被改写、文件提供程序可以是
    /// 内存或复合实现、而宿主注册静态文件的时机在本模块之后。按猜测翻转行为，
    /// 会让一个只是碰巧放了 index.html 的部署静默换掉首页。
    /// </para>
    /// </remarks>
    public bool EnableWelcomePage { get; set; } = true;

    /// <summary>
    /// 获取或设置 应用的基础路径（用于部署在子路径下，如 IIS 虚拟目录或反向代理未剥离路径的场景）
    /// 设置后将在所有中间件之前调用 UsePathBase()，把 Path 中匹配的前缀移到 PathBase，使路由能正确匹配。
    /// 示例："/myapp"（部署在 /myapp 虚拟目录下）
    /// 注意：设置 PathBase 时，建议同时将 ApiPathPrefix 设为空字符串，避免路由双重前缀。
    /// IIS 虚拟目录场景下 ANCM 会自动设置 PathBase，UsePathBase 不会重复剥离（幂等安全）。
    /// </summary>
    public string? PathBase { get; set; }

    /// <summary>
    /// 获取或设置 API 路径前缀（用于判断 API 请求，默认 "/api"）
    /// </summary>
    public string ApiPathPrefix { get; set; } = "/api";

    /// <summary>
    /// 获取或设置 是否自动包装 Action 结果为 ApiResult
    /// </summary>
    public bool AutoWrapApiResult { get; set; } = false;

    /// <summary>
    /// 获取或设置 CORS 配置选项
    /// </summary>
    public CorsOptions? Cors { get; set; }

    /// <summary>
    /// 获取或设置 Http传输加密选项
    /// </summary>
    public HttpEncryptOptions? HttpEncrypt { get; set; }

    /// <summary>
    /// 获取或设置 请求验证配置选项
    /// </summary>
    public RequestValidationOptions? RequestValidation { get; set; }

    /// <summary>
    /// 获取或设置 限流配置选项
    /// </summary>
    public RateLimitOptions? RateLimit { get; set; }

    /// <summary>
    /// 获取或设置 是否启用响应压缩
    /// </summary>
    public bool EnableResponseCompression { get; set; } = false;

    /// <summary>
    /// 获取或设置 是否启用 ForwardedHeaders（用于代理环境下的路径和协议识别）
    /// </summary>
    public bool EnableForwardedHeaders { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否启用自动路由约定
    /// 当为 true 时,框架会自动为没有显式 [Route] 特性的 Controller 生成路由
    /// 默认值：true
    /// </summary>
    public bool EnableAutoRouteConvention { get; set; } = true;

    /// <summary>
    /// 获取或设置 路由命名风格
    /// 默认值：KebabCase (推荐的 RESTful 风格)
    /// </summary>
    public RouteNamingStyle RouteNamingStyle { get; set; } = RouteNamingStyle.KebabCase;

    /// <summary>
    /// 获取或设置 是否使用复数形式的路由
    /// 例如: User -> users, Menu -> menus
    /// 默认值：true (符合 RESTful 最佳实践)
    /// </summary>
    public bool UsePluralRoutes { get; set; } = true;

    /// <summary>
    /// 获取或设置 异常处理选项
    /// 默认实例化以确保 EnableMetrics=true，让 DefaultDiagnosticsAdminController 在应用未显式配置时仍可用
    /// </summary>
    public ExceptionHandlingOptions ExceptionHandling { get; set; } = new();

    /// <summary>
    /// 获取或设置 请求追踪选项
    /// </summary>
    public RequestTrackingOptions? RequestTracking { get; set; }

    /// <summary>
    /// 获取或设置 安全头部选项
    /// </summary>
    public SecurityHeadersOptions? SecurityHeaders { get; set; }

    /// <summary>
    /// 获取或设置 API 版本控制选项
    /// </summary>
    public Versioning.ApiVersionOptions? ApiVersion { get; set; }

    /// <summary>
    /// 获取或设置 Controller 过滤选项
    /// 用于按名称通配符或程序集名称禁用特定 Controller
    /// </summary>
    public ControllerFilterOptions? ControllerFilter { get; set; }
}

/// <summary>
/// 路由命名风格枚举
/// </summary>
public enum RouteNamingStyle
{
    /// <summary>
    /// kebab-case 风格 (推荐)
    /// 例如: UserProfile -> user-profile
    /// </summary>
    KebabCase,

    /// <summary>
    /// camelCase 风格
    /// 例如: UserProfile -> userProfile
    /// </summary>
    CamelCase,

    /// <summary>
    /// PascalCase 风格
    /// 例如: UserProfile -> UserProfile
    /// </summary>
    PascalCase
}

/// <summary>
/// CORS 配置选项
/// </summary>
public class CorsOptions
{
    /// <summary>
    /// 获取或设置 是否启用 CORS
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 获取或设置 CORS 策略名称
    /// </summary>
    public string PolicyName { get; set; } = "DefaultPolicy";

    /// <summary>
    /// 获取或设置 是否允许任何源
    /// </summary>
    public bool AllowAnyOrigin { get; set; } = false;

    /// <summary>
    /// 获取或设置 允许的源列表
    /// </summary>
    public string[]? WithOrigins { get; set; }

    /// <summary>
    /// 获取或设置 是否允许任何方法
    /// </summary>
    public bool AllowAnyMethod { get; set; } = false;

    /// <summary>
    /// 获取或设置 允许的方法列表
    /// </summary>
    public string[]? WithMethods { get; set; }

    /// <summary>
    /// 获取或设置 是否允许任何头
    /// </summary>
    public bool AllowAnyHeader { get; set; } = false;

    /// <summary>
    /// 获取或设置 允许的头列表
    /// </summary>
    public string[]? WithHeaders { get; set; }

    /// <summary>
    /// 获取或设置 是否允许凭证
    /// </summary>
    public bool AllowCredentials { get; set; } = false;

    /// <summary>
    /// 获取或设置 是否禁用凭证
    /// </summary>
    public bool DisallowCredentials { get; set; } = false;
}

/// <summary>
/// Http通信加密选项
/// </summary>
public class HttpEncryptOptions
{
    /// <summary>
    /// 获取或设置 服务端私钥, 服务端生成并自己拥有的私钥
    /// </summary>
    public string? HostPrivateKey { get; set; }

    /// <summary>
    /// 获取或设置 服务端公钥, 服务端生成并分配给客户端的公钥
    /// </summary>
    public string? HostPublicKey { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Controller 过滤选项
/// 用于按名称通配符或程序集名称禁用特定 Controller，支持配置文件和代码两种方式
/// </summary>
public class ControllerFilterOptions
{
    /// <summary>
    /// 要禁用的 Controller 类名（支持 * 通配符，大小写不敏感）
    /// 例如: ["Default*"] ["*Admin*"] ["DefaultAdminAudit*", "DefaultAdminWorkflow*"]
    /// </summary>
    public string[]? DisabledControllers { get; set; }

    /// <summary>
    /// 要禁用其 Controller 的程序集名称（支持 * 通配符，大小写不敏感）
    /// 例如: ["Tnzi.Hosting"] 禁用 Hosting 模块所有 Controller
    /// </summary>
    public string[]? DisabledAssemblies { get; set; }

    /// <summary>
    /// 要禁用的<strong>单个端点</strong>，按 <see cref="SensitiveEndpointAttribute.Name"/> 匹配
    /// （支持 * 通配符，大小写不敏感）。例如: ["storage.presigned-url"] ["storage.*"]
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="DisabledControllers"/> 的区别是<strong>粒度</strong>：那个是整类开关，
    /// 想摘掉存储模块的预签名 URL 就得连上传下载一起关掉，再自己重写一个控制器。
    /// 这个只摘掉标了名字的那一个 action，控制器的其余端点照常工作。
    /// </para>
    /// <para>
    /// ★ <strong>只能摘除标了 <see cref="SensitiveEndpointAttribute"/> 的端点。</strong>
    /// 这是刻意的：一个能按任意方法名关端点的配置项，会变成绕开代码审查改 API 表面的工具，
    /// 而且没有任何东西能保证被关掉的端点不是别人正在依赖的。
    /// </para>
    /// <para>
    /// ★ <strong>配了却没匹配到任何端点时会记一条 Warning。</strong>
    /// 名字写错（<c>storage.presignedurl</c>）与"已经关掉了"在运行时长得一模一样，
    /// 而这正是本机制要消除的那类静默失效。
    /// 时机是 MVC 构建应用模型时（首个请求或 Swagger/ApiExplorer 初始化，取决于部署），
    /// 不是进程启动那一刻 —— 应用模型本身就是按需构建的。
    /// </para>
    /// </remarks>
    public string[]? DisabledEndpoints { get; set; }

    /// <summary>
    /// 自定义过滤谓词（返回 true = 保留, false = 移除）
    /// 在 DisabledControllers / DisabledAssemblies 之后执行
    /// 仅支持代码配置，不可序列化
    /// </summary>
    [JsonIgnore]
    public Func<Type, bool>? ControllerPredicate { get; set; }
}
