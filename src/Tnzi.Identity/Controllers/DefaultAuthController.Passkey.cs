using TokenResult = Tnzi.Identity.Services.TokenResult;

namespace Tnzi.Identity.Controllers;

/// <summary>
/// <see cref="DefaultAuthController"/> 的 passkey（WebAuthn）端点。
/// </summary>
/// <remarks>
/// 拆成单独文件只是为了篇幅（认证控制器已接近单文件行数上限），不是新的层次：
/// 它们与其余认证端点在同一个类、同一个路由、同样可被消费方子类化覆盖。
/// </remarks>
public partial class DefaultAuthController
{
    /// <summary>
    /// 开始注册一枚 passkey：返回喂给 <c>navigator.credentials.create()</c> 的选项。
    /// </summary>
    /// <param name="enrollmentToken">
    /// 可选的注册令牌。不给则挂到当前已登录用户名下；给了则挂到令牌指向的用户名下。
    /// </param>
    /// <remarks>
    /// ★ 路由标 <c>[AllowAnonymous]</c> 是因为持令牌注册的人<strong>本来就还没有凭据</strong>
    /// （新同事拿到邀请链接、账号恢复）。<strong>「你是谁」的判定在服务层</strong>：
    /// 既没登录也没有效令牌一律 401。判定不能挂在控制器特性上 ——
    /// 本类是 <c>[DefaultController]</c>，消费方可以在同路由整体替换掉它。
    /// </remarks>
    [HttpPost("passkey/register/begin")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<PasskeyOptionsDto>> BeginPasskeyRegistration([FromQuery] string? enrollmentToken = null)
    {
        if (PasskeyService == null)
        {
            return PasskeyUnavailable<PasskeyOptionsDto>();
        }

        var result = await PasskeyService.BeginRegistrationAsync(enrollmentToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 完成 passkey 注册：校验证明并把凭据挂到目标用户名下。
    /// </summary>
    [HttpPost("passkey/register/complete")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<PasskeyCredentialDto>> CompletePasskeyRegistration(
        [FromBody] PasskeyCompleteDto input,
        [FromQuery] string? enrollmentToken = null)
    {
        if (PasskeyService == null)
        {
            return PasskeyUnavailable<PasskeyCredentialDto>();
        }

        var result = await PasskeyService.CompleteRegistrationAsync(input, enrollmentToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 开始 passkey 登录：返回喂给 <c>navigator.credentials.get()</c> 的选项。
    /// </summary>
    /// <remarks>
    /// 匿名可访问。用户名可以不给（discoverable credential，登录页连输入框都不需要）；
    /// ★ 给了但查无此人也<strong>照常返回选项</strong>，否则这里就是个用户名枚举预言机。
    /// </remarks>
    [HttpPost("passkey/assert/begin")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<PasskeyOptionsDto>> BeginPasskeyAssertion([FromBody] PasskeyAssertionBeginDto? input = null)
    {
        if (PasskeyService == null)
        {
            return PasskeyUnavailable<PasskeyOptionsDto>();
        }

        var result = await PasskeyService.BeginAssertionAsync(input ?? new PasskeyAssertionBeginDto());
        return result.ToApiResult();
    }

    /// <summary>
    /// 完成 passkey 登录：校验断言并签发令牌。
    /// </summary>
    /// <remarks>
    /// 签发走与密码登录同一个出口，所以这条路径同样会经过登录守卫、2FA 判定与多设备策略；
    /// 该用户开着 2FA 时这里一样会返回 403 <c>2FA_REQUIRED</c>，前端按既有流程续接即可。
    /// </remarks>
    [HttpPost("passkey/assert/complete")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TokenResult>> CompletePasskeyAssertion([FromBody] PasskeyCompleteDto input)
    {
        if (PasskeyService == null)
        {
            return PasskeyUnavailable<TokenResult>();
        }

        var result = await PasskeyService.CompleteAssertionAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 列出当前用户已注册的 passkey。
    /// </summary>
    [HttpGet("passkey/credentials")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<List<PasskeyCredentialDto>>> GetPasskeyCredentials()
    {
        if (PasskeyService == null)
        {
            return PasskeyUnavailable<List<PasskeyCredentialDto>>();
        }

        var result = await PasskeyService.GetCredentialsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除当前用户的一枚 passkey。
    /// </summary>
    [HttpDelete("passkey/credentials/{credentialId}")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult> DeletePasskeyCredential(string credentialId)
    {
        if (PasskeyService == null)
        {
            return Result.Failure("Passkey is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR).ToApiResult();
        }

        var result = await PasskeyService.DeleteCredentialAsync(credentialId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 未注册 passkey 服务时的统一回答，与服务层"未启用"的回答<strong>逐字相同</strong>。
    /// </summary>
    /// <remarks>
    /// 两者不区分是刻意的：对调用方来说「这个部署没装 passkey」与「装了但没开」是同一件事，
    /// 区分开只会多暴露一点部署构成。
    /// </remarks>
    private static ApiResult<T> PasskeyUnavailable<T>()
        => Result<T>.Failure("Passkey is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR).ToApiResult();
}
