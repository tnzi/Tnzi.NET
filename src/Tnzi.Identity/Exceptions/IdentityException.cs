

namespace Tnzi.Identity.Exceptions;

/// <summary>
/// Identity 模块异常基类
/// </summary>
public class IdentityException : BusinessException
{
    /// <summary>
    /// 初始化一个<see cref="IdentityException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码</param>
    public IdentityException(string message, string errorCode = ErrorCodes.IDENTITY_ERROR, int httpStatusCode = 400)
        : base(message, errorCode, httpStatusCode)
    {
    }
}

/// <summary>
/// 用户异常
/// </summary>
public class UserException : IdentityException
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; }
    
    /// <summary>
    /// 初始化一个<see cref="UserException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="userId">用户ID</param>
    /// <param name="errorCode">错误码</param>
    public UserException(string message, string? userId = null, string errorCode = ErrorCodes.IDENTITY_USER_ERROR)
        : base(message, errorCode)
    {
        UserId = userId;
        if (userId != null)
        {
            this.WithData("UserId", userId);
        }
    }
}

/// <summary>
/// 用户未找到异常
/// </summary>
public class UserNotFoundException : UserException
{
    /// <summary>
    /// 初始化一个<see cref="UserNotFoundException"/>类型的新实例
    /// </summary>
    /// <param name="userId">用户ID</param>
    public UserNotFoundException(string userId)
        : base($"User with id '{userId}' was not found.", userId, ErrorCodes.IDENTITY_USER_NOT_FOUND)
    {
    }
}

/// <summary>
/// 用户已存在异常
/// </summary>
public class UserAlreadyExistsException : UserException
{
    /// <summary>
    /// 初始化一个<see cref="UserAlreadyExistsException"/>类型的新实例
    /// </summary>
    /// <param name="identifier">用户标识（用户名或邮箱）</param>
    public UserAlreadyExistsException(string identifier)
        : base($"User '{identifier}' already exists.", null, ErrorCodes.IDENTITY_USER_ALREADY_EXISTS)
    {
        this.WithData("Identifier", identifier);
    }
}

/// <summary>
/// 用户锁定异常
/// </summary>
public class UserLockedException : UserException
{
    /// <summary>
    /// 锁定结束时间
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; }
    
    /// <summary>
    /// 初始化一个<see cref="UserLockedException"/>类型的新实例
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="lockoutEnd">锁定结束时间</param>
    public UserLockedException(string userId, DateTimeOffset? lockoutEnd = null)
        : base($"User account is locked.", userId, ErrorCodes.IDENTITY_USER_LOCKED)
    {
        LockoutEnd = lockoutEnd;
        if (lockoutEnd.HasValue)
        {
            this.WithData("LockoutEnd", lockoutEnd.Value);
        }
    }
}

/// <summary>
/// 密码错误异常
/// </summary>
public class InvalidPasswordException : IdentityException
{
    /// <summary>
    /// 初始化一个<see cref="InvalidPasswordException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    public InvalidPasswordException(string message = "Invalid password.")
        : base(message, ErrorCodes.IDENTITY_INVALID_PASSWORD, 401)
    {
    }
}

/// <summary>
/// 密码强度不足异常
/// </summary>
public class WeakPasswordException : IdentityException
{
    /// <summary>
    /// 密码要求说明
    /// </summary>
    public string? Requirements { get; }
    
    /// <summary>
    /// 初始化一个<see cref="WeakPasswordException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="requirements">密码要求说明</param>
    public WeakPasswordException(string message = "Password is too weak.", string? requirements = null)
        : base(message, ErrorCodes.IDENTITY_PASSWORD_TOO_WEAK)
    {
        Requirements = requirements;
        if (requirements != null)
        {
            this.WithData("Requirements", requirements);
        }
    }
}

/// <summary>
/// 角色异常
/// </summary>
public class RoleException : IdentityException
{
    /// <summary>
    /// 角色ID
    /// </summary>
    public string? RoleId { get; }
    
    /// <summary>
    /// 初始化一个<see cref="RoleException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="roleId">角色ID</param>
    /// <param name="errorCode">错误码</param>
    public RoleException(string message, string? roleId = null, string errorCode = ErrorCodes.IDENTITY_ROLE_ERROR)
        : base(message, errorCode)
    {
        RoleId = roleId;
        if (roleId != null)
        {
            this.WithData("RoleId", roleId);
        }
    }
}

/// <summary>
/// 角色未找到异常
/// </summary>
public class RoleNotFoundException : RoleException
{
    /// <summary>
    /// 初始化一个<see cref="RoleNotFoundException"/>类型的新实例
    /// </summary>
    /// <param name="roleId">角色ID</param>
    public RoleNotFoundException(string roleId)
        : base($"Role with id '{roleId}' was not found.", roleId, ErrorCodes.IDENTITY_ROLE_NOT_FOUND)
    {
    }
}

/// <summary>
/// 角色已存在异常
/// </summary>
public class RoleAlreadyExistsException : RoleException
{
    /// <summary>
    /// 初始化一个<see cref="RoleAlreadyExistsException"/>类型的新实例
    /// </summary>
    /// <param name="roleName">角色名称</param>
    public RoleAlreadyExistsException(string roleName)
        : base($"Role '{roleName}' already exists.", null, ErrorCodes.IDENTITY_ROLE_ALREADY_EXISTS)
    {
        this.WithData("RoleName", roleName);
    }
}
