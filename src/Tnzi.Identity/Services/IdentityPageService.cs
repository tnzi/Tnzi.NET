namespace Tnzi.Identity.Services;

/// <summary>
/// Identity 页面生成服务实现
/// 将 HTML 页面生成逻辑从 Controller 中分离，便于维护和测试
/// </summary>
public class IdentityPageService : ApplicationService, IIdentityPageService
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// postMessage target origin（从配置读取 App:FrontendUrl，未配置时回退到 window.location.origin）
    /// </summary>
    private readonly string _postMessageOrigin;

    public IdentityPageService(
        IServiceProvider serviceProvider,
        IConfiguration? configuration = null)
        : base(serviceProvider)
    {
        _configuration = configuration;
        var frontendUrl = configuration?["App:FrontendUrl"];
        _postMessageOrigin = !string.IsNullOrEmpty(frontendUrl) ? frontendUrl.TrimEnd('/') : string.Empty;
    }

    /// <summary>
    /// 生成OAuth回调HTML页面（通过postMessage传递结果给父窗口）
    /// </summary>
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
        <div class=""message"">正在处理登录...</div>
    </div>
    <noscript>
        <div class=""container"">
            <div class=""message"" style=""color: #ff4d4f;"">请启用 JavaScript 以完成登录</div>
        </div>
    </noscript>
    <script>
        (function() {{
            try {{
                // 直接将 JSON 作为对象字面量嵌入，避免字符串转义问题
                var jsonData = {json};
                console.log('OAuth callback data:', jsonData);

                // 向父窗口发送消息
                if (window.opener && !window.opener.closed) {{
                    console.log('Sending message to opener');
                    window.opener.postMessage({{
                        type: 'oauth-callback',
                        data: jsonData
                    }}, {originJs});
                    // 延迟关闭，确保消息已发送
                    setTimeout(function() {{
                        console.log('Closing window');
                        window.close();
                    }}, 200);
                }} else {{
                    console.log('No opener window, attempting redirect');
                    // 如果没有父窗口，尝试重定向（使用 URL fragment 传递 token，避免泄露到服务器日志和 Referer 头）
                    if (jsonData.returnUrl) {{
                        var url = new URL(jsonData.returnUrl, window.location.origin);
                        if (jsonData.success && jsonData.accessToken) {{
                            var params = new URLSearchParams();
                            params.set('accessToken', jsonData.accessToken);
                            if (jsonData.refreshToken) {{
                                params.set('refreshToken', jsonData.refreshToken);
                            }}
                            url.hash = params.toString();
                        }}
                        window.location.href = url.toString();
                    }} else {{
                        document.body.innerHTML = '<div class=""container""><div class=""message"">登录成功！请关闭此窗口。</div></div>';
                    }}
                }}
            }} catch (e) {{
                console.error('OAuth callback error:', e);
                console.error('Error stack:', e.stack);
                var errorMsg = '处理登录时发生错误：' + (e.message || '未知错误') + '，请重试。';
                document.body.innerHTML = '<div class=""container""><div class=""message"" style=""color: #ff4d4f;"">' + errorMsg + '</div><div class=""message"" style=""margin-top: 1rem; font-size: 12px; color: #999;"">错误详情：' + e.toString() + '</div></div>';
            }}
        }})();
    </script>
    <script>
        // 备用检查：如果 3 秒后还没有关闭窗口，显示提示
        setTimeout(function() {{
            if (!document.body.classList.contains('closed')) {{
                console.warn('OAuth callback window still open after 3 seconds');
                var container = document.querySelector('.container');
                if (container) {{
                    var message = container.querySelector('.message');
                    if (message && message.textContent === '正在处理登录...') {{
                        message.textContent = '处理时间较长，请稍候...如果长时间无响应，请关闭窗口重试。';
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
        <button class=""close-btn"" onclick=""window.close()"">关闭窗口</button>
    </div>
    <script>
        // 向父窗口发送错误消息
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
        var title = success ? "邮箱确认成功" : "邮箱确认失败";

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
        <a href=""/"" class=""button"">返回首页</a>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// 生成重置密码表单HTML页面
    /// </summary>
    public string GenerateResetPasswordFormHtml(string email, string token)
    {
        var encodedEmail = WebUtility.HtmlEncode(email);
        // Use JSON serialization to produce a JS-safe quoted string (handles quotes, backslashes,
        // newlines, and other special characters that HtmlEncode alone would not neutralize inside
        // a JavaScript string literal context).
        var jsToken = JsonSerializer.Serialize(token);

        // 获取应用名称
        var appName = _configuration?["App:AppName"] ?? "Tnzi.NET";

        return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>重置密码 - {WebUtility.HtmlEncode(appName)}</title>
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
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;
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
                <div class=""tagline"">安全 · 可靠 · 专业</div>
            </div>

            <!-- Content -->
            <div class=""content"">
                <h1 class=""title"">重置密码</h1>
                <p class=""subtitle"">请输入您的新密码以完成重置</p>

                <form id=""resetForm"" onsubmit=""handleSubmit(event)"">
                    <div class=""form-group"">
                        <label for=""email"">邮箱地址</label>
                        <input type=""email"" id=""email"" name=""email"" value=""{encodedEmail}"" disabled required>
                    </div>
                    <div class=""form-group"">
                        <label for=""newPassword"">新密码</label>
                        <input type=""password"" id=""newPassword"" name=""newPassword"" required minlength=""6"" placeholder=""请输入新密码（至少6位）"">
                    </div>
                    <div class=""form-group"">
                        <label for=""confirmPassword"">确认密码</label>
                        <input type=""password"" id=""confirmPassword"" name=""confirmPassword"" required minlength=""6"" placeholder=""请再次输入新密码"">
                    </div>
                    <div class=""error-message"" id=""errorMessage""></div>
                    <div class=""success-message"" id=""successMessage""></div>
                    <button type=""submit"" class=""button"" id=""submitButton"">重置密码</button>
                </form>

                <div class=""divider""></div>
                <p class=""footer-text"">
                    此链接将在30分钟后过期，请及时完成重置
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

            // 隐藏之前的消息
            errorMessage.style.display = 'none';
            successMessage.style.display = 'none';

            // 验证密码
            if (newPassword !== confirmPassword) {{
                errorMessage.textContent = '两次输入的密码不一致，请重新输入';
                errorMessage.style.display = 'block';
                return;
            }}

            if (newPassword.length < 6) {{
                errorMessage.textContent = '密码长度至少为6位，请重新输入';
                errorMessage.style.display = 'block';
                return;
            }}

            // 禁用按钮
            submitButton.disabled = true;
            submitButton.textContent = '处理中...';

            // 发送请求
            fetch('/auth/reset-password', {{
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
                    successMessage.textContent = data.message || '密码重置成功！正在跳转...';
                    successMessage.style.display = 'block';
                    document.getElementById('resetForm').reset();
                    setTimeout(function() {{
                        window.location.href = '/';
                    }}, 2000);
                }} else {{
                    errorMessage.textContent = data.message || '密码重置失败，请检查链接是否有效';
                    errorMessage.style.display = 'block';
                    submitButton.disabled = false;
                    submitButton.textContent = '重置密码';
                }}
            }})
            .catch(error => {{
                errorMessage.textContent = '网络错误，请稍后重试';
                errorMessage.style.display = 'block';
                submitButton.disabled = false;
                submitButton.textContent = '重置密码';
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
        var title = success ? "密码重置成功" : "密码重置失败";

        // 获取应用名称
        var appName = _configuration?["App:AppName"] ?? "Tnzi.NET";

        return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
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
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;
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
                <div class=""tagline"">安全 · 可靠 · 专业</div>
            </div>

            <!-- Content -->
            <div class=""content"">
                <div class=""icon-wrapper"">
                    {icon}
                </div>
                <h1 class=""title"">{WebUtility.HtmlEncode(title)}</h1>
                <p class=""message"">{WebUtility.HtmlEncode(message)}</p>
                <a href=""/"" class=""button"">返回首页</a>

                <div class=""divider""></div>
                <p class=""footer-text"">
                    {WebUtility.HtmlEncode(appName)} · 感谢您的使用
                </p>
            </div>
        </div>
    </div>
</body>
</html>";
    }
}
