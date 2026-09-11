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
    /// 基于失败历史自适应难度生成验证码。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>自适应难度的验证码拼图数据</returns>
    /// <remarks>
    /// ★ <b>客户端标识由服务端自己派生</b>（当前作用域的客户端 IP），不接受调用方传入。
    /// 此前它是一个 <c>[FromQuery] clientId</c>：省略或每次换一个值就<b>永远拿到最低难度</b>，
    /// 而填别人的值可以把对方顶到最高难度。做成参数就一定会有人把请求里的值转发进来，
    /// 因此这里<b>不留参数</b> —— 让那件事在类型上不可能发生，而不是写在文档里叮嘱。
    /// 取不到客户端 IP 时按"没有失败历史"处理（与此前不传 clientId 的行为相同）。
    /// </remarks>
    Task<Result<SlidingCaptchaDto>> GenerateAdaptiveAsync(CancellationToken cancellationToken = default);
}
