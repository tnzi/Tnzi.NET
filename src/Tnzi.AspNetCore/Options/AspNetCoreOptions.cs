namespace Tnzi.AspNetCore.Options;

/// <summary>
/// ASP.NET Core 模块配置选项
/// 配置路径：AspNetCore
/// </summary>
public class AspNetCoreOptions
{
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
    /// </summary>
    public ExceptionHandlingOptions? ExceptionHandling { get; set; }

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
    /// 自定义过滤谓词（返回 true = 保留, false = 移除）
    /// 在 DisabledControllers / DisabledAssemblies 之后执行
    /// 仅支持代码配置，不可序列化
    /// </summary>
    [JsonIgnore]
    public Func<Type, bool>? ControllerPredicate { get; set; }
}
