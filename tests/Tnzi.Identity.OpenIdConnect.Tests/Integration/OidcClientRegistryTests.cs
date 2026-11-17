using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Identity.OpenIdConnect.Dtos;
using Tnzi.Identity.OpenIdConnect.Metadata;
using Tnzi.Identity.OpenIdConnect.Services;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>客户端登记簿的规则：回调地址、作用域、重复 client_id、密钥轮换、修改不丢密钥、外部引用反查、删除。</summary>
public sealed class OidcClientRegistryTests : IClassFixture<OidcProviderHost>
{
    private readonly OidcProviderHost _host;

    public OidcClientRegistryTests(OidcProviderHost host)
    {
        _host = host;
    }

    [Theory]
    [InlineData("http://rp.example.test/callback")]
    [InlineData("/relative/callback")]
    [InlineData("https://rp.example.test/callback#fragment")]
    public async Task InsecureOrMalformedRedirectUris_AreRejected(string redirectUri)
    {
        var result = await CreateAsync(new CreateOidcClientDto { DisplayName = "Bad", RedirectUris = [redirectUri] });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task LoopbackHttp_IsAccepted_ForLocalDevelopmentClients()
    {
        var result = await CreateAsync(new CreateOidcClientDto { DisplayName = "Local", RedirectUris = ["http://127.0.0.1:5173/callback"] });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task UnknownScopes_AreRejected_AndTheDefaultIsProfilePlusEmail()
    {
        var unknown = await CreateAsync(new CreateOidcClientDto { DisplayName = "X", RedirectUris = [OidcProviderHost.RedirectUri], Scopes = ["payments"] });
        unknown.Succeeded.ShouldBeFalse();
        unknown.Message!.ShouldContain("payments");

        var defaulted = await CreateAsync(new CreateOidcClientDto { DisplayName = "Y", RedirectUris = [OidcProviderHost.RedirectUri] });
        defaulted.Data!.Client.Scopes.ShouldBe(["profile", "email"], ignoreOrder: true);
    }

    [Fact]
    public async Task ADuplicateClientId_IsAConflict()
    {
        var clientId = "dup-" + Guid.NewGuid().ToString("N")[..8];
        (await CreateAsync(new CreateOidcClientDto { ClientId = clientId, DisplayName = "A", RedirectUris = [OidcProviderHost.RedirectUri] })).Succeeded.ShouldBeTrue();

        var second = await CreateAsync(new CreateOidcClientDto { ClientId = clientId, DisplayName = "B", RedirectUris = [OidcProviderHost.RedirectUri] });

        second.Code.ShouldBe(409);
    }

    [Fact]
    public async Task RotatingTheSecret_InvalidatesTheOldOne_AndUpdatingKeepsIt()
    {
        var http = _host.CreateClient();
        var login = await _host.SignInPlatformUserAsync();
        var created = (await CreateAsync(new CreateOidcClientDto
        {
            DisplayName = "Rotating",
            ConsentType = OidcConsentType.Implicit,
            RedirectUris = [OidcProviderHost.RedirectUri],
        })).Data!;
        var cookie = await _host.EstablishBridgeAsync(http, login);

        // 修改不带密钥：原密钥必须还能用。
        var updated = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().UpdateAsync(created.Client.Id, new UpdateOidcClientDto
        {
            DisplayName = "Rotating (renamed)",
            ConsentType = OidcConsentType.Implicit,
            RedirectUris = [OidcProviderHost.RedirectUri],
            Description = "after update",
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.DisplayName.ShouldBe("Rotating (renamed)");
        var original = new SampleRelyingParty(http, created.Client.ClientId, created.ClientSecret);
        (await original.RedeemCodeAsync(await original.AuthorizeAsync(cookie, "openid"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotated = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().RotateSecretAsync(created.Client.Id));
        rotated.Succeeded.ShouldBeTrue(rotated.Message);
        rotated.Data!.ClientSecret.ShouldNotBe(created.ClientSecret);

        (await original.RedeemCodeAsync(await original.AuthorizeAsync(cookie, "openid"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var renewed = new SampleRelyingParty(http, created.Client.ClientId, rotated.Data.ClientSecret);
        (await renewed.RedeemCodeAsync(await renewed.AuthorizeAsync(cookie, "openid"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicClients_HaveNoSecret_AndCannotRotateOne()
    {
        var created = (await CreateAsync(new CreateOidcClientDto { DisplayName = "SPA", ClientType = OidcClientType.Public, RedirectUris = [OidcProviderHost.RedirectUri] })).Data!;

        created.ClientSecret.ShouldBeNull();
        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().RotateSecretAsync(created.Client.Id))).Code.ShouldBe(400);
    }

    [Fact]
    public async Task ClientsCanBeFoundByExternalReference_AndDeleted()
    {
        var reference = "hosted-app-" + Guid.NewGuid().ToString("N");
        var created = (await CreateAsync(new CreateOidcClientDto { DisplayName = "Hosted", RedirectUris = [OidcProviderHost.RedirectUri], ExternalReference = reference })).Data!;

        var found = await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().FindByExternalReferenceAsync(reference));
        found.Select(c => c.Id).ShouldBe([created.Client.Id]);

        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().DeleteAsync(created.Client.Id))).Succeeded.ShouldBeTrue();
        (await _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().FindByClientIdAsync(created.Client.ClientId))).ShouldBeNull();
    }

    private Task<Tnzi.Results.Result<OidcClientSecretDto>> CreateAsync(CreateOidcClientDto input)
        => _host.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientService>().CreateAsync(input));
}
