namespace Tnzi.Swagger.Options;

/// <summary>
/// UI 提供者
/// </summary>
public enum UIProvider
{
    /// <summary>
    /// Swagger UI（默认）
    /// </summary>
    SwaggerUI = 0,

    /// <summary>
    /// Scalar（现代化 API 文档 UI）
    /// </summary>
    Scalar = 1
}

/// <summary>
/// Swagger 模块配置选项
/// 配置路径：Swagger
/// </summary>
public class SwaggerOptions
{
    /// <summary>
    /// 是否启用 Swagger（默认: true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Swagger UI 路由前缀（默认: swagger）
    /// 注意：这是路由前缀，不是完整路径。例如 "swagger" 对应路径 "/swagger"
    /// </summary>
    public string RoutePrefix { get; set; } = "swagger";

    /// <summary>
    /// 是否启用 XML 注释文档（默认: true）
    /// 启用后自动发现并加载 AppContext.BaseDirectory 下的所有 XML 文档文件
    /// </summary>
    public bool EnableXmlComments { get; set; } = true;

    /// <summary>
    /// UI 提供者（SwaggerUI 或 Scalar，默认: SwaggerUI）
    /// </summary>
    public UIProvider UIProvider { get; set; } = UIProvider.SwaggerUI;

    /// <summary>
    /// 默认文档配置
    /// </summary>
    public DefaultDocumentOptions DefaultDocument { get; set; } = new();

    /// <summary>
    /// JWT 认证配置
    /// </summary>
    public JwtAuthOptions JwtAuth { get; set; } = new();

    /// <summary>
    /// Swagger UI 配置
    /// </summary>
    public SwaggerUIOptions SwaggerUI { get; set; } = new();
}

/// <summary>
/// 默认文档配置选项
/// </summary>
public class DefaultDocumentOptions
{
    /// <summary>
    /// 文档名称（默认: v1）
    /// </summary>
    public string Name { get; set; } = "v1";

    /// <summary>
    /// 文档标题（默认: Default API）
    /// </summary>
    public string Title { get; set; } = "Default API";

    /// <summary>
    /// 文档版本（默认: v1）
    /// </summary>
    public string Version { get; set; } = "v1";

    /// <summary>
    /// 文档描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// JWT 认证配置选项
/// </summary>
public class JwtAuthOptions
{
    /// <summary>
    /// 是否启用 JWT 认证支持（默认: true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 安全方案名称（默认: Bearer）
    /// </summary>
    public string SchemeName { get; set; } = "Bearer";

    /// <summary>
    /// 安全方案描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Swagger UI 配置选项
/// </summary>
public class SwaggerUIOptions
{
    /// <summary>
    /// 是否显示请求持续时间（默认: true）
    /// </summary>
    public bool DisplayRequestDuration { get; set; } = true;

    /// <summary>
    /// 是否启用深度链接（默认: false）
    /// </summary>
    public bool EnableDeepLinking { get; set; } = false;

    /// <summary>
    /// 是否启用过滤器（默认: false）
    /// </summary>
    public bool EnableFilter { get; set; } = false;
}
