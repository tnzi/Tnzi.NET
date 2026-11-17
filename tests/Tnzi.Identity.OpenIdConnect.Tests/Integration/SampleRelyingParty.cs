using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>
/// 一个最小的依赖方：只用 HTTP 与标准协议参数（发现文档、authorize + PKCE、令牌端点、JWKS 验签、userinfo、刷新），
/// 不依赖提供方的任何内部类型 —— 一个真实的第三方应用能做到的，就是它能做到的全部。
/// </summary>
public sealed class SampleRelyingParty
{
    private readonly HttpClient _http;

    /// <summary>创建依赖方。</summary>
    public SampleRelyingParty(HttpClient http, string clientId, string? clientSecret = null)
    {
        _http = http;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    /// <summary>client_id。</summary>
    public string ClientId { get; }

    /// <summary>机密客户端的密钥（公开客户端为 null）。</summary>
    public string? ClientSecret { get; }

    /// <summary>最近一次 authorize 的 PKCE verifier。</summary>
    public string CodeVerifier { get; private set; } = string.Empty;

    /// <summary>最近一次 authorize 的 state。</summary>
    public string State { get; private set; } = string.Empty;

    /// <summary>最近一次 authorize 的 nonce。</summary>
    public string Nonce { get; private set; } = string.Empty;

    /// <summary>读发现文档。</summary>
    public async Task<JsonElement> DiscoverAsync()
    {
        var response = await _http.GetAsync("/.well-known/openid-configuration");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await OidcProviderHost.ReadJsonAsync(response);
    }

    /// <summary>构造一次 authorize 请求的地址（带新的 state / nonce / PKCE）。</summary>
    public string BuildAuthorizeUrl(string scope = "openid profile email offline_access", bool withPkce = true, string? redirectUri = null)
    {
        CodeVerifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        State = Guid.NewGuid().ToString("N");
        Nonce = Guid.NewGuid().ToString("N");

        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri ?? OidcProviderHost.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = State,
            ["nonce"] = Nonce,
        };
        if (withPkce)
        {
            parameters["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier)));
            parameters["code_challenge_method"] = "S256";
        }

        return QueryHelpers.AddQueryString("/connect/authorize", parameters);
    }

    /// <summary>
    /// 走 authorize：期望被直接送回回调地址并拿到授权码（校验 state）。
    /// </summary>
    public async Task<string> AuthorizeAsync(string cookie, string scope = "openid profile email offline_access")
    {
        var response = await OidcProviderHost.GetAsync(_http, BuildAuthorizeUrl(scope), cookie);
        response.Headers.Location?.GetLeftPart(UriPartial.Path).ShouldBe(OidcProviderHost.RedirectUri, $"authorize did not return to the RP: {response.Headers.Location}");
        var query = OidcProviderHost.RedirectQuery(response);
        query.ShouldNotContainKey("error", $"authorize failed: {string.Join(", ", query.Select(p => p.Key + "=" + p.Value))}");
        query["state"].ShouldBe(State);
        return query["code"];
    }

    /// <summary>用授权码换令牌（带 PKCE verifier）。</summary>
    public Task<HttpResponseMessage> RedeemCodeAsync(string code, string? codeVerifier = null)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = OidcProviderHost.RedirectUri,
            ["code_verifier"] = codeVerifier ?? CodeVerifier,
        });

    /// <summary>用刷新令牌换新令牌。</summary>
    public Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

    /// <summary>调 userinfo。</summary>
    public async Task<HttpResponseMessage> UserInfoAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _http.SendAsync(request);
    }

    /// <summary>按发现文档里的 JWKS 验 ID token 的签名、签发方、受众与 nonce，返回声明。</summary>
    public async Task<JsonWebToken> ValidateIdTokenAsync(string idToken)
    {
        var discovery = await DiscoverAsync();
        var jwksUri = new Uri(discovery.GetProperty("jwks_uri").GetString()!);
        var jwksResponse = await _http.GetAsync(jwksUri.PathAndQuery);
        jwksResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var jwks = new JsonWebKeySet(await jwksResponse.Content.ReadAsStringAsync());

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = discovery.GetProperty("issuer").GetString(),
            ValidAudience = ClientId,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateLifetime = true,
        });
        result.IsValid.ShouldBeTrue($"ID token did not validate: {result.Exception}");

        var token = (JsonWebToken)result.SecurityToken;
        token.GetClaim("nonce").Value.ShouldBe(Nonce);
        return token;
    }

    private async Task<HttpResponseMessage> PostTokenAsync(Dictionary<string, string> form)
    {
        form["client_id"] = ClientId;
        if (ClientSecret != null)
        {
            form["client_secret"] = ClientSecret;
        }

        return await _http.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }
}
