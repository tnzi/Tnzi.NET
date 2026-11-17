using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Identity.Entities;
using Tnzi.Identity.OpenIdConnect.Dtos;
using Tnzi.Identity.OpenIdConnect.Metadata;
using Tnzi.Identity.OpenIdConnect.Services;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>
/// 依赖方视角的端到端用例：发现文档 → 会话桥 → authorize + PKCE → 令牌端点 → JWKS 验 ID token →
/// userinfo → 刷新 → 撤销 / 停用之后刷新失败。
/// </summary>
public sealed class OidcCodeFlowTests : IClassFixture<OidcProviderHost>
{
    private readonly OidcProviderHost _host;

    public OidcCodeFlowTests(OidcProviderHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Discovery_AdvertisesTheConfiguredIssuer_TheEndpoints_AndS256()
    {
        var rp = new SampleRelyingParty(_host.CreateClient(), "unused");

        var discovery = await rp.DiscoverAsync();

        discovery.GetProperty("issuer").GetString().ShouldBe(OidcProviderHost.Issuer);
        discovery.GetProperty("authorization_endpoint").GetString().ShouldBe("https://localhost/connect/authorize");
        discovery.GetProperty("token_endpoint").GetString().ShouldBe("https://localhost/connect/token");
        discovery.GetProperty("userinfo_endpoint").GetString().ShouldBe("https://localhost/connect/userinfo");
        discovery.GetProperty("end_session_endpoint").GetString().ShouldBe("https://localhost/connect/endsession");
        discovery.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).ShouldContain("S256");
        discovery.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["openid", "profile", "email", "offline_access"], ignoreOrder: true);
    }

    [Fact]
    public async Task PublicClient_CodeFlowWithPkce_IssuesAnIdTokenCarryingTheUser_AndUserInfoAgrees()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);

        var cookie = await _host.EstablishBridgeAsync(http, login);
        var code = await rp.AuthorizeAsync(cookie);
        var tokenResponse = await rp.RedeemCodeAsync(code);
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await tokenResponse.Content.ReadAsStringAsync());
        var tokens = await OidcProviderHost.ReadJsonAsync(tokenResponse);

        var idToken = await rp.ValidateIdTokenAsync(tokens.GetProperty("id_token").GetString()!);
        idToken.Subject.ShouldBe(login.UserId.ToString());
        idToken.GetClaim("name").Value.ShouldBe(login.UserName);
        idToken.GetClaim("preferred_username").Value.ShouldBe(login.UserName);
        idToken.GetClaim("email").Value.ShouldBe(login.Email);
        idToken.GetPayloadValue<bool>("email_verified").ShouldBeTrue();

        var userInfoResponse = await rp.UserInfoAsync(tokens.GetProperty("access_token").GetString()!);
        userInfoResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await userInfoResponse.Content.ReadAsStringAsync());
        var userInfo = await OidcProviderHost.ReadJsonAsync(userInfoResponse);
        userInfo.GetProperty("sub").GetString().ShouldBe(login.UserId.ToString());
        userInfo.GetProperty("name").GetString().ShouldBe(login.UserName);
        userInfo.GetProperty("email").GetString().ShouldBe(login.Email);
        userInfo.GetProperty("email_verified").GetBoolean().ShouldBeTrue();

        // 授权码只能用一次。
        var replay = await rp.RedeemCodeAsync(code);
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scopes_TrimTheClaims_EmailVerifiedIsFalseForAnUnconfirmedAddress()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync(emailConfirmed: false);
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var openIdOnly = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie, "openid")));
        var bare = await rp.ValidateIdTokenAsync(openIdOnly.GetProperty("id_token").GetString()!);
        bare.Subject.ShouldBe(login.UserId.ToString());
        bare.TryGetClaim("name", out _).ShouldBeFalse();
        bare.TryGetClaim("email", out _).ShouldBeFalse();
        openIdOnly.TryGetProperty("refresh_token", out _).ShouldBeFalse("no offline_access, no refresh token");

        var withEmail = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie, "openid email")));
        var emailToken = await rp.ValidateIdTokenAsync(withEmail.GetProperty("id_token").GetString()!);
        emailToken.GetPayloadValue<bool>("email_verified").ShouldBeFalse();
    }

    [Fact]
    public async Task ConfidentialClient_NeedsItsSecret_AndCanRefresh()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Confidential, OidcConsentType.Implicit);
        client.ClientSecret.ShouldNotBeNullOrEmpty();
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var wrongSecret = new SampleRelyingParty(http, client.Client.ClientId, "not-the-secret");
        var rejected = await wrongSecret.RedeemCodeAsync(await wrongSecret.AuthorizeAsync(cookie));
        rejected.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var rp = new SampleRelyingParty(http, client.Client.ClientId, client.ClientSecret);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie)));
        var refreshed = await rp.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
        (await OidcProviderHost.ReadJsonAsync(refreshed)).GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task PublicClient_WithoutPkce_IsRejected()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit, requirePkce: false);
        client.Client.RequirePkce.ShouldBeTrue("public clients always require PKCE");
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var response = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl(withPkce: false), cookie);

        // OpenIddict 对「客户端要求 PKCE 却没带」直接回 400 错误页而不回跳依赖方：不给授权码就是要的结果。
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("error:invalid_request");
        body.ShouldContain("code_challenge");
    }

    [Fact]
    public async Task WithoutTheBridgeCookie_TheBrowserIsSentToTheLoginPage_WithAReturnUrlBackToAuthorize()
    {
        var http = _host.CreateClient();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);

        var response = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl());

        response.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe(OidcProviderHost.LoginUrl);
        var returnUrl = new Uri(OidcProviderHost.RedirectQuery(response)["returnUrl"]);
        returnUrl.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/connect/authorize");
        QueryHelpers.ParseQuery(returnUrl.Query)["state"].ToString().ShouldBe(rp.State);

        // prompt=none 不能交互：直接回依赖方 login_required。
        var silent = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl() + "&prompt=none");
        OidcProviderHost.RedirectQuery(silent)["error"].ShouldBe("login_required");
    }

    [Fact]
    public async Task RevokingTheUsersGrant_StopsTheRefreshToken()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie)));
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        // 用户在「我授权过的应用」里看得到它，并从那里撤销。
        using var grants = new HttpRequestMessage(HttpMethod.Get, "/api/oidc/grants");
        grants.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var listed = await OidcProviderHost.ReadJsonAsync(await http.SendAsync(grants));
        listed.GetProperty("data").EnumerateArray().Select(g => g.GetProperty("clientId").GetString()).ShouldContain(client.Client.ClientId);

        using var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/oidc/grants/{client.Client.ClientId}");
        revoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        (await http.SendAsync(revoke)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshed = await rp.RefreshAsync(refreshToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OidcProviderHost.ReadJsonAsync(refreshed)).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task AdminGrantList_NamesThePerson_NotJustTheirId()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);
        await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie));

        var page = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcGrantService>()
            .GetPagedListAsync(new OidcGrantQueryDto { ClientId = client.Client.ClientId }));

        page.Succeeded.ShouldBeTrue();
        var grant = page.Data!.Items.ShouldHaveSingleItem();
        grant.UserId.ShouldBe(login.UserId);
        grant.UserDisplayName.ShouldBe(login.UserName);
        grant.UserEmail.ShouldBe(login.Email);
    }

    [Fact]
    public async Task ProgrammaticRevocation_StopsTheRefreshToken()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie)));

        var revoked = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcGrantService>().RevokeAsync(login.UserId, client.Client.ClientId));
        revoked.ShouldBe(1);

        (await rp.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DisablingTheClient_StopsItsRefreshTokens_AndItsAuthorizeRequests_EvenAfterReEnabling()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie)));
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().SetEnabledAsync(client.Client.Id, false))).Succeeded.ShouldBeTrue();

        (await rp.RefreshAsync(refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var authorize = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl(), cookie);
        OidcProviderHost.RedirectQuery(authorize)["error"].ShouldBe("unauthorized_client");

        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().SetEnabledAsync(client.Client.Id, true))).Succeeded.ShouldBeTrue();
        (await rp.RefreshAsync(refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "re-enabling must not resurrect revoked tokens");
    }

    [Fact]
    public async Task ALockedOutUser_CannotRefresh()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);
        var tokens = await OidcProviderHost.ReadJsonAsync(await rp.RedeemCodeAsync(await rp.AuthorizeAsync(cookie)));

        await _host.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<User>>();
            var user = await users.FindByIdAsync(login.UserId.ToString());
            await users.SetLockoutEnabledAsync(user!, true);
            return await users.SetLockoutEndDateAsync(user!, DateTimeOffset.MaxValue);
        });

        (await rp.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExplicitConsent_GoesToTheConsentPage_ThenIssuesACodeOnceGranted_AndHonoursADenial()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Explicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var first = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl(), cookie);
        first.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe(OidcProviderHost.ConsentUrl);
        var consentQuery = OidcProviderHost.RedirectQuery(first);
        consentQuery["client_id"].ShouldBe(client.Client.ClientId);
        var returnUrl = consentQuery["returnUrl"];

        // 拒绝：同意页跳回 authorize 并附加 tnzi_consent=denied。
        var denied = await OidcProviderHost.GetAsync(http, returnUrl + "&tnzi_consent=denied", cookie);
        OidcProviderHost.RedirectQuery(denied)["error"].ShouldBe("access_denied");

        // 同意：前端先取展示信息，再 POST 同意，然后跳回 returnUrl。
        using var info = new HttpRequestMessage(HttpMethod.Get, $"/api/oidc/consents/{client.Client.ClientId}?scope={Uri.EscapeDataString(consentQuery["scope"])}");
        info.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var consentInfo = await OidcProviderHost.ReadJsonAsync(await http.SendAsync(info));
        consentInfo.GetProperty("data").GetProperty("alreadyGranted").GetBoolean().ShouldBeFalse();
        var scopes = consentInfo.GetProperty("data").GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!).ToList();
        scopes.ShouldBe(["openid", "profile", "email", "offline_access"], ignoreOrder: true);

        using var grant = new HttpRequestMessage(HttpMethod.Post, "/api/oidc/consents")
        {
            Content = JsonContent.Create(new { clientId = client.Client.ClientId, scopes }),
        };
        grant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        (await http.SendAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterConsent = await OidcProviderHost.GetAsync(http, returnUrl, cookie);
        var query = OidcProviderHost.RedirectQuery(afterConsent);
        query.ShouldContainKey("code");
        query["state"].ShouldBe(rp.State);
    }

    [Fact]
    public async Task TheBridge_DiesWithThePlatformSession_AndTheForwardAuthCheckFollowsIt()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync(OidcClientType.Public, OidcConsentType.Implicit);
        var rp = new SampleRelyingParty(http, client.Client.ClientId);
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var check = await OidcProviderHost.GetAsync(http, "/connect/session/check", cookie);
        check.StatusCode.ShouldBe(HttpStatusCode.OK);
        check.Headers.GetValues(OidcConstants.ForwardAuthUserIdHeader).Single().ShouldBe(login.UserId.ToString());
        check.Headers.GetValues(OidcConstants.ForwardAuthUserNameHeader).Single().ShouldBe(login.UserName);
        check.Headers.GetValues(OidcConstants.ForwardAuthUserEmailHeader).Single().ShouldBe(login.Email);
        (await OidcProviderHost.GetAsync(http, "/connect/session/check")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // 平台登出（会话被撤销）之后，同一枚 cookie 在 authorize 与 check 上都不再算数。
        await _host.WithScopeAsync(sp => sp.GetRequiredService<ISessionRevocationService>().RevokeSessionAsync(login.SessionId, SessionRevocationReason.Logout));

        (await OidcProviderHost.GetAsync(http, "/connect/session/check", cookie)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var authorize = await OidcProviderHost.GetAsync(http, rp.BuildAuthorizeUrl(), cookie);
        authorize.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe(OidcProviderHost.LoginUrl);
    }

    [Fact]
    public async Task TheBridge_RejectsATamperedCookie_AndATokenWithoutASession()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var cookie = await _host.EstablishBridgeAsync(http, login);

        (await OidcProviderHost.GetAsync(http, "/connect/session/check", cookie[..^4] + "AAAA")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sessionless = await _host.WithScopeAsync(async sp =>
        {
            var user = await sp.GetRequiredService<UserManager<User>>().FindByIdAsync(login.UserId.ToString());
            return sp.GetRequiredService<ITokenService>().GenerateToken(user!, []);
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionless);
        (await http.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var anonymous = new HttpRequestMessage(HttpMethod.Post, "/connect/session");
        (await http.SendAsync(anonymous)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EndSession_ByDefault_ClearsOnlyTheBridgeCookie()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var cookie = await _host.EstablishBridgeAsync(http, login);

        var response = await OidcProviderHost.GetAsync(http, "/connect/endsession", cookie);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("tnzi_oidc=;", StringComparison.Ordinal));
        var stillValid = await _host.WithScopeAsync(sp => sp.GetRequiredService<ISessionService>().IsSessionValidAsync(login.SessionId));
        stillValid.ShouldBeTrue("the platform session must survive an RP logout unless the opt-in switch is on");
    }

    private Task<OidcClientSecretDto> CreateClientAsync(OidcClientType type, OidcConsentType consent, bool requirePkce = true)
        => _host.WithScopeAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IOidcClientService>().CreateAsync(new CreateOidcClientDto
            {
                DisplayName = "Sample RP",
                ClientType = type,
                ConsentType = consent,
                RequirePkce = requirePkce,
                RedirectUris = [OidcProviderHost.RedirectUri],
                PostLogoutRedirectUris = [OidcProviderHost.PostLogoutRedirectUri],
                Scopes = ["profile", "email", "offline_access"],
            });
            result.Succeeded.ShouldBeTrue(result.Message);
            return result.Data!;
        });
}
