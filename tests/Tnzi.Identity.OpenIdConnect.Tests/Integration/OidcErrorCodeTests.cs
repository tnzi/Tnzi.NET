using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Exceptions;
using Tnzi.Identity.OpenIdConnect.Dtos;
using Tnzi.Identity.OpenIdConnect.Metadata;
using Tnzi.Identity.OpenIdConnect.Services;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>
/// 面向用户与管理端的 OIDC 失败都带错误码：消费方页面（同意页、「我授权过的应用」、客户端管理）按码给出本地化说明，
/// 而不是只能照搬英文消息。状态码保持不变。
/// </summary>
public sealed class OidcErrorCodeTests : IClassFixture<OidcProviderHost>
{
    private readonly OidcProviderHost _host;

    public OidcErrorCodeTests(OidcProviderHost host)
    {
        _host = host;
    }

    #region 同意页与用户自己的授权（经 HTTP，断言的是前端真正读到的 JSON）

    [Fact]
    public async Task ConsentInfo_ForAnUnknownClient_Is404WithTheClientNotFoundCode()
    {
        var login = await _host.SignInPlatformUserAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/oidc/consents/no-such-client?scope=openid", login.AccessToken);

        await ShouldFailWith(response, HttpStatusCode.NotFound, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
    }

    [Fact]
    public async Task ConsentInfo_AndGrantConsent_ForADisabledClient_Are404WithTheClientDisabledCode()
    {
        var login = await _host.SignInPlatformUserAsync();
        var client = await CreateClientAsync();
        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().SetEnabledAsync(client.Client.Id, false))).Succeeded.ShouldBeTrue();

        var info = await SendAsync(HttpMethod.Get, $"/api/oidc/consents/{client.Client.ClientId}?scope=openid", login.AccessToken);
        await ShouldFailWith(info, HttpStatusCode.NotFound, ErrorCodes.IDENTITY_OIDC_CLIENT_DISABLED);

        var grant = await SendAsync(HttpMethod.Post, "/api/oidc/consents", login.AccessToken, new { clientId = client.Client.ClientId, scopes = new[] { "openid" } });
        await ShouldFailWith(grant, HttpStatusCode.NotFound, ErrorCodes.IDENTITY_OIDC_CLIENT_DISABLED);
    }

    [Fact]
    public async Task GrantConsent_ForAnUnknownClient_Is404WithTheClientNotFoundCode()
    {
        var login = await _host.SignInPlatformUserAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/oidc/consents", login.AccessToken, new { clientId = "no-such-client", scopes = new[] { "openid" } });

        await ShouldFailWith(response, HttpStatusCode.NotFound, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
    }

    [Fact]
    public async Task RevokingMyGrants_ForAnUnknownClient_Is404WithTheClientNotFoundCode()
    {
        var login = await _host.SignInPlatformUserAsync();

        var response = await SendAsync(HttpMethod.Delete, "/api/oidc/grants/no-such-client", login.AccessToken);

        await ShouldFailWith(response, HttpStatusCode.NotFound, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
    }

    #endregion

    #region 服务层

    [Fact]
    public async Task RevokingAnUnknownGrant_CarriesTheGrantNotFoundCode()
    {
        var result = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcGrantService>().RevokeAsync(Guid.NewGuid()));

        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_OIDC_GRANT_NOT_FOUND);
    }

    [Fact]
    public async Task EveryClientOperationOnAnUnknownId_CarriesTheClientNotFoundCode()
    {
        var id = Guid.NewGuid();
        var update = new UpdateOidcClientDto { DisplayName = "X", RedirectUris = [OidcProviderHost.RedirectUri] };

        var results = await _host.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOidcClientService>();
            return new Tnzi.Results.Result[]
            {
                await service.GetAsync(id),
                await service.UpdateAsync(id, update),
                await service.SetEnabledAsync(id, false),
                await service.RotateSecretAsync(id),
                await service.DeleteAsync(id),
            };
        });

        foreach (var result in results)
        {
            result.Code.ShouldBe(404);
            result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }
    }

    [Fact]
    public async Task ADuplicateClientId_CarriesTheAlreadyExistsCode()
    {
        var clientId = "dup-" + Guid.NewGuid().ToString("N")[..8];
        (await CreateAsync(new CreateOidcClientDto { ClientId = clientId, DisplayName = "A", RedirectUris = [OidcProviderHost.RedirectUri] })).Succeeded.ShouldBeTrue();

        var second = await CreateAsync(new CreateOidcClientDto { ClientId = clientId, DisplayName = "B", RedirectUris = [OidcProviderHost.RedirectUri] });

        second.Code.ShouldBe(409);
        second.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_OIDC_CLIENT_ALREADY_EXISTS);
    }

    [Theory]
    [InlineData("http://rp.example.test/callback", null)]
    [InlineData(OidcProviderHost.RedirectUri, "payments")]
    public async Task AnInvalidRegistration_CarriesTheValidationCode(string redirectUri, string? scope)
    {
        var result = await CreateAsync(new CreateOidcClientDto
        {
            DisplayName = "Bad",
            RedirectUris = [redirectUri],
            Scopes = scope == null ? null : [scope],
        });

        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.VALIDATION_ERROR);
    }

    [Fact]
    public async Task RotatingAPublicClientsSecret_CarriesTheHasNoSecretCode()
    {
        var created = (await CreateAsync(new CreateOidcClientDto { DisplayName = "SPA", ClientType = OidcClientType.Public, RedirectUris = [OidcProviderHost.RedirectUri] })).Data!;

        var rotated = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().RotateSecretAsync(created.Client.Id));

        rotated.Code.ShouldBe(400);
        rotated.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_OIDC_CLIENT_HAS_NO_SECRET);
    }

    #endregion

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _host.CreateClient().SendAsync(request);
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.ShouldBe(status);
        var json = await OidcProviderHost.ReadJsonAsync(response);
        json.GetProperty("errorCode").GetString().ShouldBe(errorCode);
    }

    private Task<OidcClientSecretDto> CreateClientAsync()
        => _host.WithScopeAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IOidcClientService>().CreateAsync(new CreateOidcClientDto
            {
                DisplayName = "Consent RP",
                ClientType = OidcClientType.Public,
                ConsentType = OidcConsentType.Explicit,
                RedirectUris = [OidcProviderHost.RedirectUri],
            });
            result.Succeeded.ShouldBeTrue(result.Message);
            return result.Data!;
        });

    private Task<Tnzi.Results.Result<OidcClientSecretDto>> CreateAsync(CreateOidcClientDto input)
        => _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().CreateAsync(input));
}
