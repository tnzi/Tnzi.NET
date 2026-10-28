namespace Tnzi.Identity.Services;

/// <summary>
/// Identity 页面生成服务实现
/// 将 HTML 页面生成逻辑从 Controller 中分离，便于维护和测试
/// </summary>
public class IdentityPageService : ApplicationService, IIdentityPageService
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// postMessage target origin（经 <see cref="FrontendUrlResolver"/> 读前端 origin，未配置时回退到 window.location.origin）
    /// </summary>
    private readonly string _postMessageOrigin;

    private readonly IOptionsMonitor<IdentityOptions>? _identityOptions;

    private readonly IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>? _aspNetIdentityOptions;

    /// <remarks>
    /// 最后两个只被重置密码页读：链接有效期（<c>Identity:Recovery:ResetTokenExpirationMinutes</c>，运行时设置）
    /// 与 ASP.NET Identity 实际强制的最短密码长度。缺席时退回各自的默认值（30 分钟、6 位）。
    /// </remarks>
    public IdentityPageService(
        IServiceProvider serviceProvider,
        IConfiguration? configuration = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null,
        IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>? aspNetIdentityOptions = null)
        : base(serviceProvider)
    {
        _configuration = configuration;
        _identityOptions = identityOptions;
        _aspNetIdentityOptions = aspNetIdentityOptions;
        _postMessageOrigin = FrontendUrlResolver.Resolve(configuration, Logger) ?? string.Empty;
    }

    /// <summary>OAuth 回调页上「正在处理」的那句话；页面脚本据此判断还没有出结果。</summary>
    private const string ProcessingMessage = "Signing you in...";

    /// <summary>站点名缺省时页面上显示的名字。</summary>
    private const string DefaultSiteName = "Tnzi.NET";

    /// <summary>
    /// 页面抬头用的站点名：与邀请信、密码重置信同一个来源（<c>System:SiteName</c>，经 <see cref="SiteNameResolver"/>）。
    /// </summary>
    /// <remarks>
    /// 此前读的是 <c>App:AppName</c>，一个别处都不读的键：部署方配好了站点名，这两个页面上照样显示框架名。
    /// </remarks>
    private string ResolveSiteName() => SiteNameResolver.Resolve(_configuration, Logger) ?? DefaultSiteName;

    /// <summary>
    /// 生成OAuth回调HTML页面（通过postMessage传递结果给父窗口）
    /// </summary>
    /// <remarks>
    /// ★★ 页面脚本一个 console 调用都不留：载荷里就是访问令牌（Bearer 模式下还有刷新令牌），
    /// 写进控制台等于让它随排障截图、共享的控制台日志、录屏一起流出去。脚本出错时也只给一句通用的话，
    /// 不把异常对象渲染到页面上。页面里的文字与脚本注释一律英文：它们原样送到最终用户的浏览器。
    /// </remarks>
    public string GenerateOAuthCallbackHtml(OAuthCallbackResultDto result, string? returnUrl = null)
    {
        var dataObject = new
        {
            success = result.Success,
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresAt = result.ExpiresAt,
            requiresRegistration = result.RequiresRegistration,
            userInfo = result.UserInfo != null ? new
            {
                provider = result.UserInfo.Provider,
                providerKey = result.UserInfo.ProviderKey,
                email = result.UserInfo.Email,
                userName = result.UserInfo.UserName,
                displayName = result.UserInfo.DisplayName,
                avatarUrl = result.UserInfo.AvatarUrl
            } : null,
            errorMessage = result.ErrorMessage,
            // ★ 错误码与细节原样带出：登录挑战（2FA / 待办义务）就住在这两个字段里，
            // 前端按 errorCode 分支、从 errorDetails 取临时令牌，与 JSON 端点上的处理逐字相同。
            errorCode = result.ErrorCode,
            errorDetails = result.ErrorDetails,
            // 绑定流程：回调页据此知道这次没有令牌可交。关联还没发生：完成码要交回发起绑定的那个页面，
            // 由它以已登录身份调完成端点（见 OAuthCallbackResultDto.LinkCode）。
            linkedProvider = result.LinkedProvider,
            linkCode = result.LinkCode,
            returnUrl = returnUrl
        };
        // 使用 System.Text.Json 序列化为 JSON，然后作为 JavaScript 对象字面量嵌入
        // 通过将 JSON 字符串中的 </script> 替换为 <\/script> 来防止脚本注入
        var jsonString = JsonSerializer.Serialize(dataObject, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        // 转义 HTML 中的 </script> 标签，防止脚本注入
        var json = jsonString.Replace("</script>", "<\\/script>");

        // postMessage target origin：优先使用配置的 FrontendUrl，未配置时使用 window.location.origin
        var originJs = !string.IsNullOrEmpty(_postMessageOrigin)
            ? $"'{WebUtility.HtmlEncode(_postMessageOrigin)}'"
            : "window.location.origin";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>OAuth Login</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: #f5f5f5;
        }}
        .container {{
            text-align: center;
            padding: 2rem;
            background: white;
            border-radius: 8px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
        }}
        .spinner {{
            border: 3px solid #f3f3f3;
            border-top: 3px solid #1890ff;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 0 auto 1rem;
        }}
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        .message {{
            color: #666;
            font-size: 14px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""spinner""></div>
        <div class=""message"">{ProcessingMessage}</div>
    </div>
    <noscript>
        <div class=""container"">
            <div class=""message"" style=""color: #ff4d4f;"">Enable JavaScript to finish signing in.</div>
        </div>
    </noscript>
    <script>
        (function() {{
            // Nothing on this page writes to the console: the payload carries the tokens.
            function showMessage(text, isError) {{
                document.body.innerHTML = '<div class=""container""><div class=""message""></div></div>';
                var message = document.querySelector('.message');
                if (isError) {{
                    message.style.color = '#ff4d4f';
                }}
                message.textContent = text;
            }}

            try {{
                // The JSON is embedded as an object literal, so no string unescaping is needed.
                var jsonData = {json};

                // Hand the result to the window that opened this one.
                if (window.opener && !window.opener.closed) {{
                    window.opener.postMessage({{
                        type: 'oauth-callback',
                        data: jsonData
                    }}, {originJs});
                    // Close a moment later so the message is delivered first.
                    setTimeout(function() {{
                        window.close();
                    }}, 200);
                }} else {{
                    // Only a success redirects; a failure is explained here, because a challenge can only reach an opener.
                    if (!jsonData.success) {{
                        showMessage(jsonData.errorMessage || 'Sign-in was not completed. Go back to the sign-in page and try again.', true);
                    }} else if (jsonData.returnUrl) {{
                        var url = new URL(jsonData.returnUrl, window.location.origin);
                        if (jsonData.success && jsonData.accessToken) {{
                            var params = new URLSearchParams();
                            params.set('accessToken', jsonData.accessToken);
                            if (jsonData.refreshToken) {{
                                params.set('refreshToken', jsonData.refreshToken);
                            }}
                            url.hash = params.toString();
                        }} else if (jsonData.success && jsonData.linkCode) {{
                            // The link code travels in the fragment, which stays out of server logs and the Referer header.
                            var linkParams = new URLSearchParams();
                            linkParams.set('linkCode', jsonData.linkCode);
                            linkParams.set('linkedProvider', jsonData.linkedProvider || '');
                            url.hash = linkParams.toString();
                        }}
                        window.location.href = url.toString();
                    }} else if (jsonData.linkedProvider) {{
                        // Without an opener or a return address the link code cannot reach the page that started linking.
                        showMessage('Linking was not completed. Close this window and start again from your profile.', false);
                    }} else {{
                        showMessage('You are signed in. You can close this window.', false);
                    }}
                }}
            }} catch (e) {{
                // The error itself is not shown: its message may carry the tokens or the return address.
                showMessage('Something went wrong while signing you in. Close this window and try again.', true);
            }}
        }})();
    </script>
    <script>
        // Fallback hint when the window is still open after 3 seconds.
        setTimeout(function() {{
            if (!document.body.classList.contains('closed')) {{
                var container = document.querySelector('.container');
                if (container) {{
                    var message = container.querySelector('.message');
                    if (message && message.textContent === '{ProcessingMessage}') {{
                        message.textContent = 'This is taking longer than usual. If nothing happens, close this window and try again.';
                    }}
                }}
            }}
        }}, 3000);
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 生成OAuth错误HTML页面
    /// </summary>
    public string GenerateOAuthErrorHtml(string errorMessage)
    {
        var originJs = !string.IsNullOrEmpty(_postMessageOrigin)
            ? $"'{WebUtility.HtmlEncode(_postMessageOrigin)}'"
            : "window.location.origin";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>OAuth Login Error</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: #f5f5f5;
        }}
        .container {{
            text-align: center;
            padding: 2rem;
            background: white;
            border-radius: 8px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
            max-width: 400px;
        }}
        .error-icon {{
            font-size: 48px;
            color: #ff4d4f;
            margin-bottom: 1rem;
        }}
        .error-message {{
            color: #ff4d4f;
            font-size: 14px;
            margin-bottom: 1rem;
        }}
        .close-btn {{
            background: #1890ff;
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
        }}
        .close-btn:hover {{
            background: #40a9ff;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""error-icon"">⚠</div>
        <div class=""error-message"">{WebUtility.HtmlEncode(errorMessage)}</div>
        <button class=""close-btn"" onclick=""window.close()"">Close window</button>
    </div>
    <script>
        // Hand the failure to the window that opened this one.
        if (window.opener) {{
            window.opener.postMessage({{
                type: 'oauth-callback',
                data: {{
                    success: false,
                    errorMessage: {JsonSerializer.Serialize(errorMessage)}
                }}
            }}, {originJs});
        }}
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 生成邮箱确认结果HTML页面
    /// </summary>
    public string GenerateEmailConfirmationResultHtml(bool success, string message)
    {
        var iconColor = success ? "#52c41a" : "#ff4d4f";
        var icon = success ? "✓" : "✗";
        var title = success ? "Email confirmed" : "Email not confirmed";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{WebUtility.HtmlEncode(title)}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            min-height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
        }}
        .container {{
            text-align: center;
            padding: 3rem 2rem;
            background: white;
            border-radius: 16px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            max-width: 400px;
            margin: 1rem;
        }}
        .icon {{
            font-size: 64px;
            color: {iconColor};
            margin-bottom: 1.5rem;
        }}
        .title {{
            font-size: 24px;
            font-weight: 600;
            color: #1a1a1a;
            margin-bottom: 1rem;
        }}
        .message {{
            color: #666;
            font-size: 16px;
            line-height: 1.6;
            margin-bottom: 2rem;
        }}
        .button {{
            display: inline-block;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            text-decoration: none;
            padding: 12px 32px;
            border-radius: 8px;
            font-size: 16px;
            font-weight: 500;
            transition: transform 0.2s, box-shadow 0.2s;
        }}
        .button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(102, 126, 234, 0.4);
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""icon"">{icon}</div>
        <div class=""title"">{WebUtility.HtmlEncode(title)}</div>
        <div class=""message"">{WebUtility.HtmlEncode(message)}</div>
        <a href=""/"" class=""button"">Back to home</a>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// 生成重置密码表单HTML页面
    /// </summary>
    /// <remarks>
    /// 表单提交回页面自己的地址（GET 出表单、POST 收新密码，同一条路由）。此前写死 <c>/auth/reset-password</c>，
    /// 而 API 路由带着全局前缀（默认 <c>/api</c>），这个兜底页的每一次提交都是 404。
    /// </remarks>
    public string GenerateResetPasswordFormHtml(string email, string token)
    {
        var encodedEmail = WebUtility.HtmlEncode(email);
        // Use JSON serialization to produce a JS-safe quoted string (handles quotes, backslashes,
        // newlines, and other special characters that HtmlEncode alone would not neutralize inside
        // a JavaScript string literal context).
        var jsToken = JsonSerializer.Serialize(token);

        var appName = ResolveSiteName();

        // 页面上的有效期与长度提示取实际生效的值：此前写死「30 分钟」「至少 6 位」，
        // 有效期改了、或密码策略要求更长时，页面说的与服务端判的是两回事。
        var resetMinutes = _identityOptions?.CurrentValue.Recovery.ResetTokenExpirationMinutes ?? new RecoveryOptions().ResetTokenExpirationMinutes;
        var minLength = Math.Max(1, _aspNetIdentityOptions?.Value.Password.RequiredLength ?? 6);

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>Reset password - {WebUtility.HtmlEncode(appName)}</title>
    <style>
        /* Reset */
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body, table, td, a {{ -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }}

        /* Base */
        body {{
            margin: 0 !important;
            padding: 0 !important;
            width: 100% !important;
            min-height: 100vh;
            background-color: #f8f9fa;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            display: flex;
            align-items: center;
            justify-content: center;
        }}

        /* Container */
        .page-wrapper {{
            width: 100%;
            max-width: 500px;
            margin: 20px;
        }}

        .page-container {{
            background-color: #ffffff;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 4px 24px rgba(0, 0, 0, 0.06);
        }}

        /* Header */
        .header {{
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
            padding: 48px 40px;
            text-align: center;
        }}

        .logo {{
            font-size: 28px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: -0.5px;
            margin-bottom: 8px;
        }}

        .logo-accent {{
            display: inline-block;
            width: 8px;
            height: 8px;
            background: linear-gradient(135deg, #e94560 0%, #ff6b6b 100%);
            border-radius: 50%;
            margin-left: 2px;
            vertical-align: super;
        }}

        .tagline {{
            font-size: 14px;
            color: rgba(255, 255, 255, 0.7);
            letter-spacing: 2px;
            text-transform: uppercase;
        }}

        /* Content */
        .content {{
            padding: 48px 40px;
        }}

        .title {{
            font-size: 24px;
            font-weight: 600;
            color: #1a1a2e;
            margin-bottom: 8px;
            line-height: 1.3;
        }}

        .subtitle {{
            font-size: 14px;
            color: #718096;
            margin-bottom: 32px;
        }}

        .form-group {{
            margin-bottom: 24px;
        }}

        label {{
            display: block;
            font-size: 14px;
            font-weight: 500;
            color: #374151;
            margin-bottom: 8px;
        }}

        input[type=""email""],
        input[type=""password""] {{
            width: 100%;
            padding: 14px 16px;
            font-size: 16px;
            border: 2px solid #e5e7eb;
            border-radius: 8px;
            transition: all 0.2s ease;
            box-sizing: border-box;
            background-color: #ffffff;
        }}

        input[type=""email""]:focus,
        input[type=""password""]:focus {{
            outline: none;
            border-color: #e94560;
            box-shadow: 0 0 0 3px rgba(233, 69, 96, 0.1);
        }}

        input[type=""email""]:disabled {{
            background-color: #f3f4f6;
            cursor: not-allowed;
            color: #6b7280;
        }}

        .button {{
            width: 100%;
            background: linear-gradient(135deg, #e94560 0%, #ff6b6b 100%);
            color: #ffffff !important;
            border: none;
            padding: 16px 32px;
            border-radius: 50px;
            font-size: 16px;
            font-weight: 600;
            letter-spacing: 0.5px;
            cursor: pointer;
            transition: all 0.3s ease;
            box-shadow: 0 4px 16px rgba(233, 69, 96, 0.3);
            margin-top: 8px;
        }}

        .button:hover:not(:disabled) {{
            box-shadow: 0 6px 24px rgba(233, 69, 96, 0.4);
            transform: translateY(-2px);
        }}

        .button:active:not(:disabled) {{
            transform: translateY(0);
        }}

        .button:disabled {{
            opacity: 0.6;
            cursor: not-allowed;
            transform: none;
        }}

        .error-message {{
            color: #dc2626;
            font-size: 14px;
            margin-top: 8px;
            padding: 12px;
            background-color: #fef2f2;
            border-left: 4px solid #dc2626;
            border-radius: 4px;
            display: none;
        }}

        .success-message {{
            color: #16a34a;
            font-size: 14px;
            margin-top: 8px;
            padding: 12px;
            background-color: #f0fdf4;
            border-left: 4px solid #16a34a;
            border-radius: 4px;
            display: none;
        }}

        .divider {{
            height: 1px;
            background: linear-gradient(90deg, transparent 0%, #e2e8f0 50%, transparent 100%);
            margin: 32px 0;
        }}

        .footer-text {{
            text-align: center;
            font-size: 13px;
            color: #718096;
            margin-top: 24px;
        }}

        /* Responsive */
        @media screen and (max-width: 600px) {{
            .page-wrapper {{
                margin: 12px;
            }}

            .header {{
                padding: 36px 24px;
            }}

            .logo {{
                font-size: 24px;
            }}

            .content {{
                padding: 36px 24px;
            }}

            .title {{
                font-size: 20px;
            }}
        }}

        /* Dark Mode Support */
        @media (prefers-color-scheme: dark) {{
            body {{
                background-color: #0d1117 !important;
            }}

            .page-container {{
                background-color: #161b22 !important;
            }}

            .title {{
                color: #f0f6fc !important;
            }}

            .subtitle {{
                color: #8b949e !important;
            }}

            label {{
                color: #c9d1d9 !important;
            }}

            input[type=""email""],
            input[type=""password""] {{
                background-color: #21262d !important;
                border-color: #30363d !important;
                color: #f0f6fc !important;
            }}

            input[type=""email""]:focus,
            input[type=""password""]:focus {{
                border-color: #e94560 !important;
            }}

            input[type=""email""]:disabled {{
                background-color: #161b22 !important;
                color: #6b7280 !important;
            }}

            .footer-text {{
                color: #8b949e !important;
            }}
        }}
    </style>
</head>
<body>
    <div class=""page-wrapper"">
        <div class=""page-container"">
            <!-- Header -->
            <div class=""header"">
                <div class=""logo"">
                    {WebUtility.HtmlEncode(appName)}<span class=""logo-accent""></span>
                </div>
            </div>

            <!-- Content -->
            <div class=""content"">
                <h1 class=""title"">Reset password</h1>
                <p class=""subtitle"">Enter a new password to finish resetting it.</p>

                <form id=""resetForm"" onsubmit=""handleSubmit(event)"">
                    <div class=""form-group"">
                        <label for=""email"">Email address</label>
                        <input type=""email"" id=""email"" name=""email"" value=""{encodedEmail}"" disabled required>
                    </div>
                    <div class=""form-group"">
                        <label for=""newPassword"">New password</label>
                        <input type=""password"" id=""newPassword"" name=""newPassword"" required minlength=""{minLength}"" placeholder=""At least {minLength} characters"">
                    </div>
                    <div class=""form-group"">
                        <label for=""confirmPassword"">Confirm password</label>
                        <input type=""password"" id=""confirmPassword"" name=""confirmPassword"" required minlength=""{minLength}"" placeholder=""Enter the new password again"">
                    </div>
                    <div class=""error-message"" id=""errorMessage""></div>
                    <div class=""success-message"" id=""successMessage""></div>
                    <button type=""submit"" class=""button"" id=""submitButton"">Reset password</button>
                </form>

                <div class=""divider""></div>
                <p class=""footer-text"">
                    This link expires {resetMinutes} minutes after it was sent.
                </p>
            </div>
        </div>
    </div>
    <script>
        function handleSubmit(event) {{
            event.preventDefault();

            var email = document.getElementById('email').value;
            var token = {jsToken};
            var newPassword = document.getElementById('newPassword').value;
            var confirmPassword = document.getElementById('confirmPassword').value;
            var errorMessage = document.getElementById('errorMessage');
            var successMessage = document.getElementById('successMessage');
            var submitButton = document.getElementById('submitButton');

            // Hide earlier messages.
            errorMessage.style.display = 'none';
            successMessage.style.display = 'none';

            // Check the passwords.
            if (newPassword !== confirmPassword) {{
                errorMessage.textContent = 'The passwords do not match.';
                errorMessage.style.display = 'block';
                return;
            }}

            if (newPassword.length < {minLength}) {{
                errorMessage.textContent = 'The password must be at least {minLength} characters long.';
                errorMessage.style.display = 'block';
                return;
            }}

            // Disable the button while the request runs.
            submitButton.disabled = true;
            submitButton.textContent = 'Resetting...';

            // Post back to this page's own address: the same route serves the form (GET) and takes the new password (POST).
            fetch(window.location.pathname, {{
                method: 'POST',
                headers: {{
                    'Content-Type': 'application/json'
                }},
                body: JSON.stringify({{
                    email: email,
                    token: token,
                    newPassword: newPassword
                }})
            }})
            .then(response => response.json())
            .then(data => {{
                if (data.succeeded) {{
                    successMessage.textContent = data.message || 'Your password has been reset. Redirecting...';
                    successMessage.style.display = 'block';
                    document.getElementById('resetForm').reset();
                    setTimeout(function() {{
                        window.location.href = '/';
                    }}, 2000);
                }} else {{
                    errorMessage.textContent = data.message || 'The password could not be reset. Check that the link is still valid.';
                    errorMessage.style.display = 'block';
                    submitButton.disabled = false;
                    submitButton.textContent = 'Reset password';
                }}
            }})
            .catch(error => {{
                errorMessage.textContent = 'Network error. Please try again later.';
                errorMessage.style.display = 'block';
                submitButton.disabled = false;
                submitButton.textContent = 'Reset password';
            }});
        }}
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 生成重置密码结果HTML页面
    /// </summary>
    public string GenerateResetPasswordResultHtml(bool success, string message)
    {
        var iconColor = success ? "#16a34a" : "#dc2626";
        var iconBgColor = success ? "#f0fdf4" : "#fef2f2";
        var icon = success ? "✓" : "✗";
        var title = success ? "Password reset" : "Password not reset";

        var appName = ResolveSiteName();

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>{WebUtility.HtmlEncode(title)} - {WebUtility.HtmlEncode(appName)}</title>
    <style>
        /* Reset */
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body, table, td, a {{ -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }}

        /* Base */
        body {{
            margin: 0 !important;
            padding: 0 !important;
            width: 100% !important;
            min-height: 100vh;
            background-color: #f8f9fa;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            display: flex;
            align-items: center;
            justify-content: center;
        }}

        /* Container */
        .page-wrapper {{
            width: 100%;
            max-width: 500px;
            margin: 20px;
        }}

        .page-container {{
            background-color: #ffffff;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 4px 24px rgba(0, 0, 0, 0.06);
        }}

        /* Header */
        .header {{
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
            padding: 48px 40px;
            text-align: center;
        }}

        .logo {{
            font-size: 28px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: -0.5px;
            margin-bottom: 8px;
        }}

        .logo-accent {{
            display: inline-block;
            width: 8px;
            height: 8px;
            background: linear-gradient(135deg, #e94560 0%, #ff6b6b 100%);
            border-radius: 50%;
            margin-left: 2px;
            vertical-align: super;
        }}

        .tagline {{
            font-size: 14px;
            color: rgba(255, 255, 255, 0.7);
            letter-spacing: 2px;
            text-transform: uppercase;
        }}

        /* Content */
        .content {{
            padding: 48px 40px;
            text-align: center;
        }}

        .icon-wrapper {{
            width: 80px;
            height: 80px;
            background: {iconBgColor};
            border-radius: 50%;
            margin: 0 auto 24px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 48px;
            color: {iconColor};
        }}

        .title {{
            font-size: 24px;
            font-weight: 600;
            color: #1a1a2e;
            margin-bottom: 16px;
            line-height: 1.3;
        }}

        .message {{
            color: #4a5568;
            font-size: 16px;
            line-height: 1.7;
            margin-bottom: 32px;
        }}

        .button {{
            display: inline-block;
            background: linear-gradient(135deg, #e94560 0%, #ff6b6b 100%);
            color: #ffffff !important;
            text-decoration: none;
            padding: 16px 40px;
            border-radius: 50px;
            font-size: 16px;
            font-weight: 600;
            letter-spacing: 0.5px;
            transition: all 0.3s ease;
            box-shadow: 0 4px 16px rgba(233, 69, 96, 0.3);
        }}

        .button:hover {{
            box-shadow: 0 6px 24px rgba(233, 69, 96, 0.4);
            transform: translateY(-2px);
        }}

        .divider {{
            height: 1px;
            background: linear-gradient(90deg, transparent 0%, #e2e8f0 50%, transparent 100%);
            margin: 32px 0;
        }}

        .footer-text {{
            font-size: 13px;
            color: #718096;
            margin-top: 24px;
        }}

        /* Responsive */
        @media screen and (max-width: 600px) {{
            .page-wrapper {{
                margin: 12px;
            }}

            .header {{
                padding: 36px 24px;
            }}

            .logo {{
                font-size: 24px;
            }}

            .content {{
                padding: 36px 24px;
            }}

            .title {{
                font-size: 20px;
            }}
        }}

        /* Dark Mode Support */
        @media (prefers-color-scheme: dark) {{
            body {{
                background-color: #0d1117 !important;
            }}

            .page-container {{
                background-color: #161b22 !important;
            }}

            .title {{
                color: #f0f6fc !important;
            }}

            .message {{
                color: #8b949e !important;
            }}

            .footer-text {{
                color: #8b949e !important;
            }}
        }}
    </style>
</head>
<body>
    <div class=""page-wrapper"">
        <div class=""page-container"">
            <!-- Header -->
            <div class=""header"">
                <div class=""logo"">
                    {WebUtility.HtmlEncode(appName)}<span class=""logo-accent""></span>
                </div>
            </div>

            <!-- Content -->
            <div class=""content"">
                <div class=""icon-wrapper"">
                    {icon}
                </div>
                <h1 class=""title"">{WebUtility.HtmlEncode(title)}</h1>
                <p class=""message"">{WebUtility.HtmlEncode(message)}</p>
                <a href=""/"" class=""button"">Back to home</a>

                <div class=""divider""></div>
                <p class=""footer-text"">
                    {WebUtility.HtmlEncode(appName)}
                </p>
            </div>
        </div>
    </div>
</body>
</html>";
    }
}
