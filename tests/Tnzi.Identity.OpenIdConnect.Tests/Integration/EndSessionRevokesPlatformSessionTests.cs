using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Identity.OpenIdConnect.Dtos;
using Tnzi.Identity.OpenIdConnect.Metadata;
using Tnzi.Identity.OpenIdConnect.Services;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>打开 <c>EndSessionRevokesPlatformSession</c> 的宿主。</summary>
public sealed class RevokingEndSessionHost : OidcProviderHost
{
    /// <inheritdoc />
    protected override IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
    {
        ["Identity:OpenIdConnect:EndSessionRevokesPlatformSession"] = "true",
    };
}

/// <summary>
/// 开关打开时，依赖方发起的登出也撤销平台会话 —— 但只在请求带着该用户的 <c>id_token_hint</c> 时。
/// </summary>
public sealed class EndSessionRevokesPlatformSessionTests : IClassFixture<RevokingEndSessionHost>
{
    private readonly RevokingEndSessionHost _host;

    public EndSessionRevokesPlatformSessionTests(RevokingEndSessionHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task EndSession_WithTheUsersIdTokenHint_RevokesThePlatformSession_AndReturnsToTheRp()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var rp = new SampleRelyingParty(http, await CreateClientIdAsync());
        var cookie = await _host.EstablishBridgeAsync(http, login);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie, "openid")));

        var url = QueryHelpers.AddQueryString("/connect/endsession", new Dictionary<string, string?>
        {
            ["id_token_hint"] = tokens.GetProperty("id_token").GetString(),
            ["post_logout_redirect_uri"] = OidcProviderHost.PostLogoutRedirectUri,
        });
        var response = await OidcProviderHost.GetAsync(http, url, cookie);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe(OidcProviderHost.PostLogoutRedirectUri);
        (await IsPlatformSessionValidAsync(login)).ShouldBeFalse();
    }

    [Fact]
    public async Task EndSession_WithoutAHint_OnlyClearsTheCookie_SoACrossSiteLinkCannotSignTheUserOut()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var response = await OidcProviderHost.GetAsync(http, "/connect/endsession", cookie);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("tnzi_oidc=;", StringComparison.Ordinal));
        (await IsPlatformSessionValidAsync(login)).ShouldBeTrue();
    }

    private Task<bool> IsPlatformSessionValidAsync(PlatformLogin login)
        => _host.WithScopeAsync(sp => sp.GetRequiredService<ISessionService>().IsSessionValidAsync(login.SessionId));

    private Task<string> CreateClientIdAsync()
        => _host.WithScopeAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IOidcClientService>().CreateAsync(new CreateOidcClientDto
            {
                DisplayName = "Logout RP",
                ClientType = OidcClientType.Public,
                ConsentType = OidcConsentType.Implicit,
                RedirectUris = [OidcProviderHost.RedirectUri],
                PostLogoutRedirectUris = [OidcProviderHost.PostLogoutRedirectUri],
            });
            result.Succeeded.ShouldBeTrue(result.Message);
            return result.Data!.Client.ClientId;
        });
}
