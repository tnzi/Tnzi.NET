using Microsoft.Extensions.Configuration;
using OpenIddict.Server;

namespace Tnzi.Identity.OpenIdConnect;

/// <summary>
/// Identity 的 OpenID Connect 提供方子模块：让应用成为其它 Web 应用的身份提供方
/// （「用平台账号登录」）。
/// </summary>
/// <remarks>
/// <b>业务范围一句话</b>：别的应用把用户送来，平台确认是谁、按用户同意的范围签出 ID token 与访问令牌。
/// <br/><br/>
/// <b>协议实现交给 OpenIddict</b>（服务端 + EF 存储），本模块只做框架那一侧的接线：
/// 用户与声明来自平台账号；「用户已登录」来自平台会话（经会话桥，见 <see cref="IOidcSessionBridge"/>）；
/// 客户端登记、授权撤销、管理端点与权限码按框架惯例提供；密钥按密钥环配置。
/// <br/><br/>
/// <b>谁会刻意不加载它</b>：不需要让任何外部应用凭平台账号登录的宿主 —— 绝大多数应用。
/// 不加载时平台自身的登录（JwtBearer、刷新令牌、会话）一个字节不变：本模块只<b>新增</b>端点与一个认证处理器，
/// 不改默认认证方案。
/// <br/><br/>
/// <b>表前缀沿用 <c>Identity</c></b>（<c>Identity_OidcClient</c> 等四张表），与其它 Identity 子模块同形。
/// </remarks>
[DependsOn(typeof(IdentityModule))]
public class IdentityOpenIdConnectModule : TnziApplicationModule
{
    /// <inheritdoc />
    public override string? TableNamePrefix => IdentityConstants.TablePrefix;

    /// <inheritdoc />
    /// <remarks>7：Identity(0)、Presence(5)、Organization(6) 之后。实际次序由 <c>[DependsOn]</c> 保证，此值仅为同级 tiebreak。</remarks>
    public override int LoadOrder => 7;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        Check.NotNull(context);
        var services = context.Services;

        services.AddTnziOptions<OpenIdConnectOptions, OpenIdConnectOptionsValidator>(context.Configuration);
        services.AddTransient<IPermissionDefinitionProvider, IdentityOpenIdConnectPermissions>();

        // 作用域与传输安全要在注册期定下（OpenIddict 的构建器在这里收它们），故直接读配置节。
        var settings = context.Configuration.GetSection("Identity:OpenIdConnect").Get<OpenIdConnectOptions>() ?? new OpenIdConnectOptions();

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore()
                .ReplaceDefaultEntities<OidcClient, OidcAuthorization, OidcScope, OidcToken, Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(OidcConstants.AuthorizePath)
                    .SetTokenEndpointUris(OidcConstants.TokenPath)
                    .SetUserInfoEndpointUris(OidcConstants.UserInfoPath)
                    .SetEndSessionEndpointUris(OidcConstants.EndSessionPath)
                    .SetRevocationEndpointUris(OidcConstants.RevocationPath);

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow();

                server.RegisterScopes([.. OidcConstants.StandardScopes, .. settings.AdditionalScopes ?? []]);

                // 访问令牌签名但不加密：资源方（以及将来的代理网关）可以凭 JWKS 自己验它。
                // 授权码与刷新令牌只有本服务读，照常加密。
                server.DisableAccessTokenEncryption();

                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();

                if (!settings.RequireHttps)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });

        // 签发方、寿命与密钥：密钥校验与「开发环境临时密钥」要看宿主环境，放在选项配置器里。
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, OidcServerOptionsSetup>();

        // OpenIddict 的 EF 存储从这里拿 DbContext：解析为应用的主上下文。
        services.AddScoped<IOpenIddictEntityFrameworkCoreContext, OidcPrimaryDbContextAccessor>();

        services.AddScoped<IOidcSessionBridge, OidcSessionBridge>();
        services.AddScoped<OidcPrincipalFactory>();
        services.AddScoped<IOidcClientService, OidcClientService>();
        services.AddScoped<IOidcGrantService, OidcGrantService>();

        return base.ConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 主动解析一次服务端选项：密钥缺失或格式错误在启动时失败，而不是在第一个依赖方来登录时。
    /// </remarks>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        Check.NotNull(context);
        _ = context.ServiceProvider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
        return base.OnApplicationInitializationAsync(context);
    }
}
