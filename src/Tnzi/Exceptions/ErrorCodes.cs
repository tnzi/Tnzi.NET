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
    public const string IDENTITY_EMAIL_NOT_SET = "IDENTITY_EMAIL_NOT_SET";
    public const string IDENTITY_EMAIL_ALREADY_CONFIRMED = "IDENTITY_EMAIL_ALREADY_CONFIRMED";
    public const string IDENTITY_EMAIL_NOT_CONFIRMED = "IDENTITY_EMAIL_NOT_CONFIRMED";
    public const string IDENTITY_TOKEN_INVALID = "IDENTITY_TOKEN_INVALID";
    public const string IDENTITY_2FA_REQUIRED = "2FA_REQUIRED";
    public const string IDENTITY_SESSION_ALREADY_ACTIVE = "IDENTITY_SESSION_ALREADY_ACTIVE";
    public const string IDENTITY_SESSION_LIMIT_REACHED = "IDENTITY_SESSION_LIMIT_REACHED";
    public const string IDENTITY_SESSION_REVOKED = "IDENTITY_SESSION_REVOKED";
    public const string IDENTITY_CAPTCHA_REQUIRED = "IDENTITY_CAPTCHA_REQUIRED";

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
