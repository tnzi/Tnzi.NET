namespace Tnzi.Identity.Controllers;

/// <summary>
/// <see cref="DefaultAuthController"/> 的第三方登录部分：发起授权、回调落地，
/// 以及回调页要用到的 <c>returnUrl</c> 白名单。
/// </summary>
/// <remarks>
/// <para>
/// 按登录方式分 partial：第三方登录自成一段（外部提供方、回调、跳转白名单，
/// 与本地口令登录没有共享状态），同 <c>DefaultAuthController.Passkey.cs</c> /
/// <c>.StepUp.cs</c> / <c>.TokenDelivery.cs</c>。类、路由、可覆盖性一律不变。
/// </para>
/// <para>
/// ★★★ 这一段里最要紧的一条：<b>回调页会把令牌放进跳转目标的 URL fragment</b>，
/// 所以 <c>returnUrl</c> 必须过白名单，且进出各校验一次 —— 它中途经
/// <c>Identity.External</c> cookie 往返了一圈，而校验一个从外部回来的值，
/// 不能只靠「它进来的时候查过了」。详见 <c>ReturnUrlValidator</c>。
/// </para>
/// </remarks>
public partial class DefaultAuthController
{
    /// <summary>
    /// 发起OAuth第三方登录（跳转到第三方登录页面）
    /// </summary>
    /// <param name="provider">OAuth提供者名称（不区分大小写，支持 Google、Microsoft、Facebook、Twitter、GitHub）</param>
    /// <param name="returnUrl">登录成功后的回调地址（可选，前端页面URL）</param>
    /// <param name="linkToken">
    /// 个人中心「绑定第三方账号」签发的一次性令牌（<c>POST users/profile/linked-accounts/{provider}/link-token</c>）。
    /// 带上它，这次流程就是<b>绑定</b>而不是登录：回调把外部登录挂到签发令牌的账号上，不签发令牌、不新建账号。
    /// 无效 / 过期 / 提供商不符一律 400，当场拒绝而不是让用户走完同意页才失败。
    /// </param>
    /// <returns>重定向到第三方登录页面</returns>
    [HttpGet("oauth/{provider:regex((?i)google|microsoft|facebook|twitter|github)}/login")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> OAuthLogin(string provider, [FromQuery] string? returnUrl = null, [FromQuery] string? linkToken = null)
    {
        // 验证 Provider 是否已配置
        var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();

        // 规范化 provider 名称（首字母大写，以匹配注册的 scheme 名称）
        var schemeName = NormalizeProviderName(provider);
        var scheme = schemes.FirstOrDefault(s => s.Name.Equals(schemeName, StringComparison.OrdinalIgnoreCase));

        if (scheme == null)
        {
            return new BadRequestObjectResult(BadRequest<string>($"OAuth provider '{provider}' is not configured"));
        }

        // 构建回调处理端点的 URL（OAuth 中间件完成后重定向到这里）
        var callbackHandlerUrl = Url.Action(nameof(OAuthCallbackHandler), new { provider = provider.ToLowerInvariant() });

        // 配置认证属性
        var properties = new AuthenticationProperties
        {
            RedirectUri = callbackHandlerUrl,
            Items =
            {
                ["LoginProvider"] = scheme.Name
            }
        };

        // ★★★ 校验必须在这里，而不是「回调时再说」。这条 URL 会一路带到回调页，
        // 由页面脚本 `location.href = returnUrl + "#accessToken=…"` 跳过去 ——
        // 不校验就等于任何人都能构造一条链接，把受害者的令牌送到自己的站点上。
        // 拒绝而不是静默丢弃：前端配错了白名单要看得见，否则表现是「登录完停在空白页」。
        if (!string.IsNullOrEmpty(returnUrl))
        {
            if (!ReturnUrlValidator.IsAllowed(returnUrl, AllowedReturnOrigins))
            {
                Logger.LogWarning("Rejected an OAuth login with a disallowed returnUrl.");
                return new BadRequestObjectResult(
                    BadRequest<string>("returnUrl is not an allowed destination"));
            }

            properties.Items["returnUrl"] = returnUrl;
        }

        // ★ 绑定令牌：这里只校验不消费（用户可能在同意页放弃），写进 Items 经 Identity.External cookie
        //   签名往返到回调，回调再消费。整页跳转没有 bearer，这枚令牌是「谁在绑定」的唯一来源。
        if (!string.IsNullOrEmpty(linkToken))
        {
            var peek = OAuthLinkTokens == null
                ? Result<Guid>.Failure("OAuth link service is not available", 400)
                : await OAuthLinkTokens.PeekAsync(linkToken, provider);
            if (!peek.Succeeded)
            {
                Logger.LogWarning("Rejected an OAuth link attempt for {Provider} with an invalid link token.", provider);
                return new BadRequestObjectResult(BadRequest<string>(peek.Message ?? "Invalid or expired link token"));
            }

            properties.Items[LinkTokenItemKey] = linkToken;
        }

        // 发起 Challenge，重定向到第三方登录页面
        return Challenge(properties, scheme.Name);
    }

    /// <summary><c>AuthenticationProperties.Items</c> 里承载绑定令牌的键。</summary>
    private const string LinkTokenItemKey = "linkToken";

    /// <summary>
    /// OAuth回调处理端点（OAuth 中间件完成认证后重定向到这里）
    /// 注意：这个端点与 OAuth 中间件的 CallbackPath 不同
    /// CallbackPath 是中间件拦截的路径（如 /auth/oauth/google-callback）
    /// 这个端点是中间件完成后重定向到的路径（如 /auth/oauth/google/callback）
    /// </summary>
    /// <param name="provider">OAuth提供者名称</param>
    /// <returns>OAuth回调结果HTML页面</returns>
    [HttpGet("oauth/{provider:regex((?i)google|microsoft|facebook|twitter|github)}/callback")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> OAuthCallbackHandler(string provider)
    {
        if (OAuthService == null)
        {
            return Content(GenerateOAuthErrorHtml("OAuth service is not available"), "text/html; charset=utf-8");
        }

        try
        {
            // 从 Identity.External scheme 获取认证结果
            var authenticateResult = await HttpContext.AuthenticateAsync("Identity.External");

            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                var errorMessage = authenticateResult.Failure?.Message ?? "OAuth authentication failed";
                return Content(GenerateOAuthErrorHtml(errorMessage), "text/html; charset=utf-8");
            }

            // 获取 returnUrl。★ 这里**再校验一次**：这个值经 Identity.External cookie 往返了一圈，
            // 而校验一个从外部回来的值不能只靠「它进来的时候查过了」。复校的成本是一次纯函数调用。
            var returnUrl = authenticateResult.Properties?.Items.ContainsKey("returnUrl") == true
                ? authenticateResult.Properties.Items["returnUrl"]
                : null;
            if (!ReturnUrlValidator.IsAllowed(returnUrl, AllowedReturnOrigins))
            {
                Logger.LogWarning("Dropped a disallowed returnUrl on the OAuth callback.");
                returnUrl = null;
            }

            // 添加IP地址和UserAgent到Claims
            var claims = authenticateResult.Principal.Claims.ToList();
            // 走 GetClientIp：支持反向代理，且受 AspNetCoreOptions.CollectClientIpAddress 约束
            // （该 claim 会流向登录日志，声明不采集地址的部署这里应当是空）。
            claims.Add(new Claim("ip_address", HttpContext.Request.GetClientIp() ?? ""));
            claims.Add(new Claim("user_agent", HttpContext.Request.Headers["User-Agent"].ToString()));

            var claimsPrincipal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, authenticateResult.Principal.Identity?.AuthenticationType));

            // ★ 绑定流程：令牌在，就只做「把外部身份挂到签发者账号上」——不签发令牌、不新建账号、不按邮箱认领。
            //   这条分支必须在 HandleOAuthCallbackAsync 之前：那条路的兜底是新建账号并签发令牌。
            var linkToken = authenticateResult.Properties?.Items.TryGetValue(LinkTokenItemKey, out var lt) == true ? lt : null;
            if (!string.IsNullOrEmpty(linkToken))
            {
                return await CompleteLinkAsync(provider.ToLowerInvariant(), linkToken, claimsPrincipal, returnUrl);
            }

            // 处理OAuth回调
            var result = await OAuthService.HandleOAuthCallbackAsync(provider.ToLowerInvariant(), claimsPrincipal);

            if (!result.Succeeded)
            {
                // ★★★ 带错误码的失败是**结构化**的，不能塞进错误页。
                // 共享签发出口用失败信封承载 2FA 挑战与待办义务（403 + 错误码 + 临时令牌），
                // 而错误页的 postMessage 只有 { success:false, errorMessage } —— 走那条路
                // 等于把挑战丢掉，前端只会看到一句「OAuth callback failed」，
                // 于是开着 2FA 的账号在第三方登录上根本走不通。
                // 无错误码的失败（服务缺失、第三方认证本身失败、异常）才是真正的错误页场景。
                if (!string.IsNullOrEmpty(result.ErrorCode))
                {
                    var challenge = new OAuthCallbackResultDto
                    {
                        Success = false,
                        ErrorMessage = result.Message,
                        ErrorCode = result.ErrorCode,
                        ErrorDetails = result.ErrorDetails,
                    };
                    return Content(GenerateOAuthCallbackHtml(challenge, returnUrl), "text/html; charset=utf-8");
                }

                return Content(GenerateOAuthErrorHtml(result.Message ?? "OAuth callback failed"), "text/html; charset=utf-8");
            }

            // 清除 Identity.External cookie
            await HttpContext.SignOutAsync("Identity.External");

            // 生成回调HTML（cookie 模式下先把刷新令牌移进 cookie，页面拿不到它）
            var html = GenerateOAuthCallbackHtml(DeliverOAuthTokens(result.Data!), returnUrl);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            return Content(GenerateOAuthErrorHtml($"OAuth callback error: {ex.Message}"), "text/html; charset=utf-8");
        }
    }

    /// <summary>
    /// 绑定流程的回调落地：消费绑定令牌 → 把外部登录挂到签发者账号上 → 回调页只跳回 returnUrl，不带任何令牌。
    /// </summary>
    private async Task<IActionResult> CompleteLinkAsync(string provider, string linkToken, ClaimsPrincipal principal, string? returnUrl)
    {
        var consumed = OAuthLinkTokens == null
            ? Result<Guid>.Failure("OAuth link service is not available", 400)
            : await OAuthLinkTokens.ConsumeAsync(linkToken, provider);
        if (!consumed.Succeeded)
        {
            return Content(GenerateOAuthErrorHtml(consumed.Message ?? "Invalid or expired link token"), "text/html; charset=utf-8");
        }

        var linked = await OAuthService!.LinkExternalLoginAsync(consumed.Data, provider, principal);

        await HttpContext.SignOutAsync("Identity.External");

        if (!linked.Succeeded)
        {
            // 带错误码的失败（provider key 已属他人 → 409）走结构化回调页，与登录挑战同一条路；前端按 errorCode 分支。
            var failure = new OAuthCallbackResultDto
            {
                Success = false,
                ErrorMessage = linked.Message,
                ErrorCode = linked.ErrorCode ?? ErrorCodes.IDENTITY_OAUTH_ERROR,
                LinkedProvider = provider,
            };
            return Content(GenerateOAuthCallbackHtml(failure, returnUrl), "text/html; charset=utf-8");
        }

        var success = new OAuthCallbackResultDto { Success = true, LinkedProvider = provider };
        return Content(GenerateOAuthCallbackHtml(success, returnUrl), "text/html; charset=utf-8");
    }

    /// <summary>
    /// 实际生效的 returnUrl 白名单：<c>Identity:OAuth:AllowedReturnOrigins</c> 优先，
    /// 未配置时回退前端 origin（<see cref="FrontendUrlResolver"/>），两者皆空则只放行站内相对路径。
    /// </summary>
    protected IReadOnlyCollection<string> AllowedReturnOrigins
        => ReturnUrlValidator.ResolveAllowedOrigins(
            IdentityOptions?.CurrentValue?.OAuth?.AllowedReturnOrigins,
            FrontendUrlResolver.Resolve(Configuration, Logger));

    /// <summary>
    /// 规范化 OAuth 提供者名称（首字母大写）
    /// </summary>
    private static string NormalizeProviderName(string provider)
    {
        if (string.IsNullOrEmpty(provider)) return provider;
        return char.ToUpperInvariant(provider[0]) + provider[1..].ToLowerInvariant();
    }

    /// <summary>
    /// 生成OAuth回调HTML页面（通过postMessage传递结果给父窗口）
    /// </summary>
    protected virtual string GenerateOAuthCallbackHtml(OAuthCallbackResultDto result, string? returnUrl = null)
        => IdentityPageService?.GenerateOAuthCallbackHtml(result, returnUrl)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    /// <summary>
    /// 生成OAuth错误HTML页面
    /// </summary>
    protected virtual string GenerateOAuthErrorHtml(string errorMessage)
        => IdentityPageService?.GenerateOAuthErrorHtml(errorMessage)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");
}
