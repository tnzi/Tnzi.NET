namespace Tnzi.Identity.OpenIdConnect.Options;

/// <summary>
/// 校验 <see cref="OpenIdConnectOptions"/>。密钥环在这里<b>真读一遍</b>（解析 PEM、核对长度与 Active 标识），
/// 格式错误在启动期报出而不是在第一次签发时。
/// </summary>
/// <remarks>
/// 「生产环境一把密钥都没配」不在这里判：验证器不知道宿主环境，那一条由模块在初始化阶段经
/// <c>IHostEnvironment</c> 判定后拒绝启动。
/// </remarks>
public class OpenIdConnectOptionsValidator : OptionsValidatorBase<OpenIdConnectOptions>
{
    /// <inheritdoc />
    protected override string GetConfigurationPath(string? name) => "Identity:OpenIdConnect";

    /// <inheritdoc />
    protected override void ValidateOptions(OpenIdConnectOptions options, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(options.Issuer)
            && (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) || !string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment)))
        {
            AddError(errors, nameof(options.Issuer), "must be an absolute URL without query or fragment.");
        }

        ValidateRedirectTarget(options.LoginUrl, nameof(options.LoginUrl), errors);
        ValidateRedirectTarget(options.ConsentUrl, nameof(options.ConsentUrl), errors);

        if (string.IsNullOrWhiteSpace(options.ReturnUrlParameter))
        {
            AddError(errors, nameof(options.ReturnUrlParameter), "is required.");
        }

        RequirePositive(options.AccessTokenLifetime, nameof(options.AccessTokenLifetime), errors);
        RequirePositive(options.IdentityTokenLifetime, nameof(options.IdentityTokenLifetime), errors);
        RequirePositive(options.AuthorizationCodeLifetime, nameof(options.AuthorizationCodeLifetime), errors);
        RequirePositive(options.RefreshTokenLifetime, nameof(options.RefreshTokenLifetime), errors);

        var cookie = options.SessionCookie;
        if (cookie == null)
        {
            AddError(errors, nameof(options.SessionCookie), "is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(cookie.Name))
            {
                AddError(errors, "SessionCookie:Name", "is required.");
            }

            if (string.IsNullOrWhiteSpace(cookie.Path) || !cookie.Path.StartsWith('/'))
            {
                AddError(errors, "SessionCookie:Path", "must start with '/'.");
            }

            RequirePositive(cookie.Lifetime, "SessionCookie:Lifetime", errors);
        }

        foreach (var scope in options.AdditionalScopes ?? [])
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsWhiteSpace))
            {
                AddError(errors, nameof(options.AdditionalScopes), $"'{scope}' is not a valid scope name.");
            }
        }

        var keyErrors = new List<string>();
        OidcKeyMaterial.Load(options, keyErrors);
        foreach (var error in keyErrors)
        {
            AddError(errors, "Keys", error);
        }
    }

    private void ValidateRedirectTarget(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var isRelative = value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal);
        var isAbsolute = Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        if (!isRelative && !isAbsolute)
        {
            AddError(errors, name, "must be an absolute http(s) URL or a path starting with '/'.");
        }
    }

    private void RequirePositive(TimeSpan value, string name, List<string> errors)
    {
        if (value <= TimeSpan.Zero)
        {
            AddError(errors, name, "must be positive.");
        }
    }
}
