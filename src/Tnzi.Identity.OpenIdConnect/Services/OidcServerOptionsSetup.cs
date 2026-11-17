using Microsoft.Extensions.Hosting;
using OpenIddict.Server;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 把 <see cref="OpenIdConnectOptions"/> 落到 OpenIddict 的服务端选项上：签发方、令牌寿命、签名与加密凭据。
/// </summary>
/// <remarks>
/// ★ 一把密钥都没配时：Development 用进程内临时密钥并记 Warning；其它环境抛 <see cref="ConfigurationException"/>。
/// 模块在初始化阶段主动解析一次服务端选项，所以这个异常出现在启动时，而不是第一个依赖方来登录的时候。
/// </remarks>
public sealed class OidcServerOptionsSetup : IConfigureOptions<OpenIddictServerOptions>
{
    private readonly IOptions<OpenIdConnectOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OidcServerOptionsSetup> _logger;

    /// <summary>创建配置器。</summary>
    public OidcServerOptionsSetup(IOptions<OpenIdConnectOptions> options, IHostEnvironment environment, ILogger<OidcServerOptionsSetup> logger)
    {
        _options = Check.NotNull(options);
        _environment = Check.NotNull(environment);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public void Configure(OpenIddictServerOptions options)
    {
        Check.NotNull(options);
        var settings = _options.Value;

        if (!string.IsNullOrWhiteSpace(settings.Issuer))
        {
            options.Issuer = new Uri(settings.Issuer, UriKind.Absolute);
        }

        options.AccessTokenLifetime = settings.AccessTokenLifetime;
        options.IdentityTokenLifetime = settings.IdentityTokenLifetime;
        options.AuthorizationCodeLifetime = settings.AuthorizationCodeLifetime;
        options.RefreshTokenLifetime = settings.RefreshTokenLifetime;

        var errors = new List<string>();
        var keys = OidcKeyMaterial.Load(settings, errors);
        if (errors.Count > 0)
        {
            throw new ConfigurationException("Identity:OpenIdConnect", "Invalid OpenID Connect key configuration: " + string.Join(" ", errors));
        }

        if (keys.IsEmpty && _environment.IsDevelopment())
        {
            _logger.LogWarning(
                "Identity:OpenIdConnect has no signing or encryption keys; using ephemeral development keys. " +
                "Tokens stop validating on restart and differ between instances. Configure SigningKeys and EncryptionKeys (or KeyFile) before deploying.");
            keys = OidcKeyMaterial.CreateEphemeral();
        }

        if (!keys.IsComplete)
        {
            throw new ConfigurationException(
                "Identity:OpenIdConnect",
                "The OpenID Connect provider needs at least one signing key (SigningKeys) and one encryption key (EncryptionKeys), in configuration or in KeyFile. " +
                "Ephemeral keys are only generated in the Development environment.");
        }

        options.SigningCredentials.AddRange(keys.SigningCredentials);
        options.EncryptionCredentials.AddRange(keys.EncryptingCredentials);
    }
}
