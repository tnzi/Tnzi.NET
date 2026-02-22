
namespace Tnzi.Services;

/// <summary>
/// 验证码服务接口
/// </summary>
public interface IVerifyCodeService
{
    /// <summary>
    /// 生成验证码
    /// </summary>
    /// <param name="codeLength">验证码长度（默认4位）</param>
    /// <param name="codeType">验证码类型（数字、字母、混合）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证码信息（包含验证码文本和图片字节）</returns>
    Task<VerifyCodeResult> GenerateAsync(int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成验证码（指定唯一标识）
    /// </summary>
    /// <param name="id">唯一标识（用于存储验证码）</param>
    /// <param name="codeLength">验证码长度（默认4位）</param>
    /// <param name="codeType">验证码类型（数字、字母、混合）</param>
    /// <param name="expireMinutes">过期时间（分钟，默认5分钟）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证码信息（包含验证码文本和图片字节）</returns>
    Task<VerifyCodeResult> GenerateAsync(string id, int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, int expireMinutes = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证验证码
    /// </summary>
    /// <param name="id">唯一标识</param>
    /// <param name="code">用户输入的验证码</param>
    /// <param name="ignoreCase">是否忽略大小写（默认true）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果</returns>
    Task<bool> VerifyAsync(string id, string code, bool ignoreCase = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除验证码
    /// </summary>
    /// <param name="id">唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 验证码结果
/// </summary>
public class VerifyCodeResult
{
    /// <summary>
    /// 获取或设置 验证码文本
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 验证码图片字节
    /// </summary>
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 获取或设置 唯一标识（用于后续验证）
    /// </summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// 验证码类型
/// </summary>
public enum VerifyCodeType
{
    /// <summary>
    /// 数字
    /// </summary>
    Number,

    /// <summary>
    /// 字母
    /// </summary>
    Letter,

    /// <summary>
    /// 混合（数字+字母）
    /// </summary>
    Mixed
}