namespace Tnzi.Identity.Services;

/// <summary>
/// 验证码服务接口
/// </summary>
public interface ICaptchaService
{
    /// <summary>
    /// 检查缓存服务是否可用
    /// </summary>
    bool IsCacheAvailable { get; }

    /// <summary>
    /// 生成验证码
    /// </summary>
    /// <param name="purpose">用途（login, register 等）</param>
    /// <returns>验证码ID和图片数据</returns>
    /// <exception cref="InvalidOperationException">缓存服务不可用时抛出</exception>
    Task<CaptchaResult> GenerateAsync(string purpose);

    /// <summary>
    /// 验证验证码
    /// </summary>
    /// <param name="captchaId">验证码ID</param>
    /// <param name="captchaCode">用户输入的验证码</param>
    /// <param name="purpose">用途</param>
    /// <returns>是否验证成功</returns>
    Task<bool> VerifyAsync(string captchaId, string captchaCode, string purpose);

    /// <summary>
    /// 记录登录失败（用于 CaptchaFailThreshold 功能）
    /// </summary>
    /// <param name="identifier">标识符（用户名或IP地址）</param>
    Task RecordLoginFailureAsync(string identifier);

    /// <summary>
    /// 获取登录失败次数
    /// </summary>
    /// <param name="identifier">标识符</param>
    /// <returns>失败次数</returns>
    Task<int> GetLoginFailureCountAsync(string identifier);

    /// <summary>
    /// 清除登录失败记录（登录成功后调用）
    /// </summary>
    /// <param name="identifier">标识符</param>
    Task ClearLoginFailureAsync(string identifier);

    /// <summary>
    /// 检查是否需要验证码（基于失败次数阈值）
    /// </summary>
    /// <param name="identifier">标识符</param>
    /// <returns>是否需要验证码</returns>
    Task<bool> IsCaptchaRequiredAsync(string identifier);
}

/// <summary>
/// 验证码生成结果
/// </summary>
public class CaptchaResult
{
    /// <summary>
    /// 验证码ID（客户端需要在提交时返回）
    /// </summary>
    public string CaptchaId { get; set; } = string.Empty;

    /// <summary>
    /// 验证码图片字节数组（PNG格式）
    /// </summary>
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 验证码过期时间（秒）
    /// </summary>
    public int ExpirationSeconds { get; set; }
}

