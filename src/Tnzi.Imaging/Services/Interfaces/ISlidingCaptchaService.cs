namespace Tnzi.Imaging.Services;

/// <summary>
/// 滑动验证码服务接口
/// </summary>
public interface ISlidingCaptchaService
{
    /// <summary>
    /// 生成滑动验证码拼图
    /// </summary>
    /// <param name="options">可选的自定义配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证码拼图数据</returns>
    Task<Result<SlidingCaptchaDto>> GenerateAsync(SlidingCaptchaOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证用户滑动结果
    /// </summary>
    /// <param name="token">验证令牌</param>
    /// <param name="userX">用户滑动的 X 坐标</param>
    /// <param name="tolerance">
    /// 容差像素值的回退值。实际生效的容差由生成时的服务端决策（配置的
    /// <c>SlidingCaptcha.Tolerance</c> 或自适应难度调紧后的值）随令牌一起存下，
    /// 仅当令牌里没有记录容差时才采用此入参。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果</returns>
    Task<Result<SlidingCaptchaVerifyResult>> VerifyAsync(string token, int userX, int tolerance = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于失败历史自适应难度生成验证码
    /// </summary>
    /// <param name="clientId">客户端标识（用于追踪失败次数）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>自适应难度的验证码拼图数据</returns>
    Task<Result<SlidingCaptchaDto>> GenerateAdaptiveAsync(string? clientId = null, CancellationToken cancellationToken = default);
}
