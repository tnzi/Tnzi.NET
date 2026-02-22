namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 请求验证器接口
/// </summary>
public interface IRequestValidator
{
    /// <summary>
    /// 验证请求
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <returns>验证结果，如果验证通过返回 null，否则返回错误消息</returns>
    Task<string?> ValidateAsync(HttpContext context);
}

