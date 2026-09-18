namespace Tnzi.Exceptions;

/// <summary>
/// 错误码常量定义
/// 命名规范：[模块前缀]_[错误类型]_[具体错误]
/// </summary>
public static class ErrorCodes
{
    // ==================== 通用错误码 ====================
    public const string UNKNOWN_ERROR = "UNKNOWN_ERROR";
    public const string INTERNAL_SERVER_ERROR = "INTERNAL_SERVER_ERROR";
    public const string BUSINESS_ERROR = "BUSINESS_ERROR";
    public const string VALIDATION_ERROR = "VALIDATION_ERROR";

    // ==================== 资源相关错误码 ====================
    public const string RESOURCE_NOT_FOUND = "RESOURCE_NOT_FOUND";
    public const string RESOURCE_ALREADY_EXISTS = "RESOURCE_ALREADY_EXISTS";

    // ==================== 认证和授权错误码 ====================
    public const string UNAUTHORIZED = "UNAUTHORIZED";
    public const string FORBIDDEN = "FORBIDDEN";
    public const string INVALID_CREDENTIALS = "INVALID_CREDENTIALS";
    public const string TOKEN_EXPIRED = "TOKEN_EXPIRED";
    public const string TOKEN_INVALID = "TOKEN_INVALID";

    // ==================== 数据相关错误码 ====================
    public const string DATA_CONFLICT = "DATA_CONFLICT";
    public const string DATA_INVALID = "DATA_INVALID";
    public const string DATA_DUPLICATE = "DATA_DUPLICATE";

    // ==================== 服务相关错误码 ====================
    public const string SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE";
    public const string SERVICE_TIMEOUT = "SERVICE_TIMEOUT";
    public const string RATE_LIMIT_EXCEEDED = "RATE_LIMIT_EXCEEDED";

    // ==================== 人机验证错误码 ====================
    /// <summary>
    /// 端点要求人机验证而请求没有带有效令牌（<c>[RequireCaptcha]</c> 与消费方自己调 <c>ICaptchaVerifier</c> 时用）。
    /// Identity 的登录 / 注册流程沿用 <see cref="IDENTITY_CAPTCHA_REQUIRED"/>，那一条随响应附带一道新题。
    /// </summary>
    public const string CAPTCHA_REQUIRED = "CAPTCHA_REQUIRED";

    // ==================== 配置相关错误码 ====================
    public const string CONFIGURATION_ERROR = "CONFIGURATION_ERROR";
    public const string CONFIGURATION_MISSING = "CONFIGURATION_MISSING";
    public const string CONFIGURATION_INVALID = "CONFIGURATION_INVALID";

    // ==================== 模块相关错误码 ====================
    public const string MODULE_ERROR = "MODULE_ERROR";
    public const string MODULE_LOAD_FAILED = "MODULE_LOAD_FAILED";
    public const string MODULE_CIRCULAR_DEPENDENCY = "MODULE_CIRCULAR_DEPENDENCY";

    // ==================== Identity 模块错误码 ====================
    public const string IDENTITY_ERROR = "IDENTITY_ERROR";
    public const string IDENTITY_USER_ERROR = "IDENTITY_USER_ERROR";
    public const string IDENTITY_USER_NOT_FOUND = "IDENTITY_USER_NOT_FOUND";
    public const string IDENTITY_USER_ALREADY_EXISTS = "IDENTITY_USER_ALREADY_EXISTS";
    public const string IDENTITY_USER_LOCKED = "IDENTITY_USER_LOCKED";

    /// <summary>
    /// 账号已开好但本人还没接受邀请。所有登录路径与找回密码路径共用这一个码，
    /// 前端据它引导用户去点邀请链接，而不是让人一遍遍地重置密码。
    /// </summary>
    public const string IDENTITY_ACTIVATION_PENDING = "IDENTITY_ACTIVATION_PENDING";

    /// <summary>邀请令牌无效、已用过或已过期。三种情况共用一个码，刻意不区分。</summary>
    public const string IDENTITY_INVITATION_INVALID = "IDENTITY_INVITATION_INVALID";

    /// <summary>
    /// 凭据已经过关，但账号欠着必须先办完的事（改密码之类）。
    /// </summary>
    /// <remarks>
    /// ★ 这不是「登录失败」。前端见到它应当停在登录流程里、渲染对应的那一步，
    /// 而不是把人退回登录页 —— 与 <see cref="IDENTITY_2FA_REQUIRED"/> 同一形态。
    /// </remarks>
    public const string IDENTITY_PENDING_ACTIONS_REQUIRED = "IDENTITY_PENDING_ACTIONS_REQUIRED";
    public const string IDENTITY_INVALID_PASSWORD = "IDENTITY_INVALID_PASSWORD";
    public const string IDENTITY_PASSWORD_TOO_WEAK = "IDENTITY_PASSWORD_TOO_WEAK";
    public const string IDENTITY_ROLE_ERROR = "IDENTITY_ROLE_ERROR";
    public const string IDENTITY_ROLE_NOT_FOUND = "IDENTITY_ROLE_NOT_FOUND";
    public const string IDENTITY_ROLE_ALREADY_EXISTS = "IDENTITY_ROLE_ALREADY_EXISTS";
    public const string IDENTITY_ROLE_SYSTEM_PROTECTED = "IDENTITY_ROLE_SYSTEM_PROTECTED";
    public const string IDENTITY_USER_CREATE_FAILED = "IDENTITY_USER_CREATE_FAILED";
    public const string IDENTITY_USER_UPDATE_FAILED = "IDENTITY_USER_UPDATE_FAILED";
    public const string IDENTITY_USER_DELETE_FAILED = "IDENTITY_USER_DELETE_FAILED";
    public const string IDENTITY_ROLE_ASSIGN_FAILED = "IDENTITY_ROLE_ASSIGN_FAILED";
    public const string IDENTITY_ROLE_REMOVE_FAILED = "IDENTITY_ROLE_REMOVE_FAILED";
    public const string IDENTITY_ORGANIZATION_ERROR = "IDENTITY_ORGANIZATION_ERROR";
    public const string IDENTITY_ORGANIZATION_NOT_FOUND = "IDENTITY_ORGANIZATION_NOT_FOUND";
    public const string IDENTITY_PASSWORD_CHANGE_FAILED = "IDENTITY_PASSWORD_CHANGE_FAILED";
    public const string IDENTITY_PASSWORD_RESET_FAILED = "IDENTITY_PASSWORD_RESET_FAILED";
    public const string IDENTITY_OAUTH_ERROR = "IDENTITY_OAUTH_ERROR";

    /// <summary>
    /// 第三方身份带来的邮箱未被提供商证实过，因此<b>不自动关联</b>到同邮箱的既有账号。
    /// </summary>
    /// <remarks>
    /// ★★★ 按邮箱自动认领账号，前提是那个邮箱确实属于登录者。有些提供商的资料邮箱
    /// 是用户自己填的、从不校验（GitHub 的 profile email、Facebook / Twitter），
    /// 于是「用受害者的邮箱注册一个第三方账号，再用它登录」就能接管本地账号。
    /// 拿不到「已验证」这个断言时，正确的做法不是拒绝这个人，而是<b>不替他认领账号</b>：
    /// 让他用常规方式登录一次，再从个人中心主动绑定 —— 那时「他是不是账号主人」已经被证明过了。
    /// </remarks>
    public const string IDENTITY_OAUTH_LINK_CONFIRMATION_REQUIRED = "IDENTITY_OAUTH_LINK_CONFIRMATION_REQUIRED";
    public const string IDENTITY_EMAIL_NOT_SET = "IDENTITY_EMAIL_NOT_SET";
    public const string IDENTITY_EMAIL_ALREADY_CONFIRMED = "IDENTITY_EMAIL_ALREADY_CONFIRMED";
    public const string IDENTITY_EMAIL_NOT_CONFIRMED = "IDENTITY_EMAIL_NOT_CONFIRMED";
    public const string IDENTITY_TOKEN_INVALID = "IDENTITY_TOKEN_INVALID";
    public const string IDENTITY_2FA_REQUIRED = "2FA_REQUIRED";
    public const string IDENTITY_SESSION_ALREADY_ACTIVE = "IDENTITY_SESSION_ALREADY_ACTIVE";
    public const string IDENTITY_SESSION_LIMIT_REACHED = "IDENTITY_SESSION_LIMIT_REACHED";
    public const string IDENTITY_SESSION_REVOKED = "IDENTITY_SESSION_REVOKED";

    /// <summary>
    /// 检测到刷新令牌重放：一枚已经被轮换掉的刷新令牌在宽限窗之外又被使用。
    /// 整条会话已被撤销，客户端必须重新登录（不要重试刷新）。
    /// </summary>
    /// <remarks>
    /// ★ 刻意<b>与「令牌无效」用不同的码</b>，尽管两者返回的文案相同。
    /// 收到它的那一方多半是<b>合法用户</b>（谁先刷新谁赢，输的一方可能是真人），
    /// 而「你的会话因为安全原因被结束了，请重新登录」与「登录过期了」对用户是两件事。
    /// 对攻击者它不构成新信息：会话此刻已经死了，知道自己被发现并不能换来任何东西。
    /// </remarks>
    public const string IDENTITY_REFRESH_TOKEN_REUSED = "IDENTITY_REFRESH_TOKEN_REUSED";

    /// <summary>
    /// 请求的客户端特征与会话建立时不一致（令牌很可能已被搬到别的设备上使用）。
    /// 会话已撤销，需要重新认证。
    /// </summary>
    public const string IDENTITY_SESSION_BINDING_MISMATCH = "IDENTITY_SESSION_BINDING_MISMATCH";

    public const string IDENTITY_CAPTCHA_REQUIRED = "IDENTITY_CAPTCHA_REQUIRED";
    public const string IDENTITY_STEP_UP_REQUIRED = "IDENTITY_STEP_UP_REQUIRED";

    // ==================== FileStorage 模块错误码 ====================
    public const string FILE_STORAGE_ERROR = "FILE_STORAGE_ERROR";
    public const string FILE_NOT_FOUND = "FILE_NOT_FOUND";
    public const string FILE_UPLOAD_ERROR = "FILE_UPLOAD_ERROR";
    public const string FILE_DOWNLOAD_ERROR = "FILE_DOWNLOAD_ERROR";
    public const string FILE_DELETE_ERROR = "FILE_DELETE_ERROR";
    public const string FILE_SIZE_EXCEEDED = "FILE_SIZE_EXCEEDED";
    public const string FILE_TYPE_NOT_SUPPORTED = "FILE_TYPE_NOT_SUPPORTED";
    public const string FILE_VERSION_NOT_ENABLED = "FILE_VERSION_NOT_ENABLED";
    public const string FILE_SHARING_NOT_ENABLED = "FILE_SHARING_NOT_ENABLED";
    public const string FILE_CHUNKED_UPLOAD_NOT_ENABLED = "FILE_CHUNKED_UPLOAD_NOT_ENABLED";
    public const string FILE_OPERATION_ERROR = "FILE_OPERATION_ERROR";

    // ==================== System 模块错误码 ====================
    public const string SYSTEM_ERROR = "SYSTEM_ERROR";

    // ==================== Notification 模块错误码 ====================
    public const string NOTIFICATION_ERROR = "NOTIFICATION_ERROR";
    public const string NOTIFICATION_EMAIL_ERROR = "NOTIFICATION_EMAIL_ERROR";
    public const string NOTIFICATION_SMS_ERROR = "NOTIFICATION_SMS_ERROR";
    public const string NOTIFICATION_PUSH_ERROR = "NOTIFICATION_PUSH_ERROR";
    public const string NOTIFICATION_CANCELLED = "NOTIFICATION_CANCELLED";
    public const string NOTIFICATION_SEND_ERROR = "NOTIFICATION_SEND_ERROR";

    // ==================== Template 模块错误码 ====================
    public const string TEMPLATE_ERROR = "TEMPLATE_ERROR";
    public const string TEMPLATE_NOT_FOUND = "TEMPLATE_NOT_FOUND";
    public const string TEMPLATE_RENDER_ERROR = "TEMPLATE_RENDER_ERROR";
    public const string TEMPLATE_COMPILATION_ERROR = "TEMPLATE_COMPILATION_ERROR";
    public const string TEMPLATE_SECURITY_ERROR = "TEMPLATE_SECURITY_ERROR";

    // ==================== 基础设施错误码 ====================

    // 数据库
    public const string DATABASE_ERROR = "DATABASE_ERROR";
    public const string DATABASE_CONNECTION_FAILED = "DATABASE_CONNECTION_FAILED";
    public const string DATABASE_MIGRATION_FAILED = "DATABASE_MIGRATION_FAILED";
    public const string DATABASE_CONCURRENCY_ERROR = "DATABASE_CONCURRENCY_ERROR";
    public const string DATABASE_QUERY_ERROR = "DATABASE_QUERY_ERROR";

    // 缓存
    public const string CACHE_ERROR = "CACHE_ERROR";
    public const string CACHE_CONNECTION_FAILED = "CACHE_CONNECTION_FAILED";
    public const string CACHE_READ_ERROR = "CACHE_READ_ERROR";
    public const string CACHE_WRITE_ERROR = "CACHE_WRITE_ERROR";

    // 消息队列
    public const string MESSAGE_QUEUE_ERROR = "MESSAGE_QUEUE_ERROR";
    public const string RABBITMQ_ERROR = "RABBITMQ_ERROR";
    public const string RABBITMQ_CONNECTION_FAILED = "RABBITMQ_CONNECTION_FAILED";
    public const string KAFKA_ERROR = "KAFKA_ERROR";
    public const string KAFKA_CONNECTION_FAILED = "KAFKA_CONNECTION_FAILED";

    // 外部服务
    public const string EXTERNAL_SERVICE_ERROR = "EXTERNAL_SERVICE_ERROR";
    public const string EXTERNAL_API_ERROR = "EXTERNAL_API_ERROR";
    public const string EXTERNAL_API_TIMEOUT = "EXTERNAL_API_TIMEOUT";
}
