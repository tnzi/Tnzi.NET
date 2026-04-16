using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Tnzi.AI.Engine.Context;

namespace Tnzi.AI.Tests.Engine.Context;

public class ClaimContextProviderTests
{
    private static ClaimContextProvider CreateSut(IEnumerable<Claim> claims, ClaimInjectionOptions opts)
    {
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new ClaimContextProvider(accessor, Microsoft.Extensions.Options.Options.Create(opts));
    }

    [Fact]
    public async Task GetContext_WithMatchingPrefix_ReturnsFormattedString()
    {
        var sut = CreateSut(
            [
                new Claim("ai:locale", "zh-CN"),
                new Claim("ai:tone", "formal"),
                new Claim("unrelated", "x")
            ],
            new ClaimInjectionOptions { ClaimPrefix = "ai:" });

        var result = await sut.GetContextAsync([]);

        result.ShouldNotBeNull();
        result.Messages.ShouldNotBeNull();
        result.Messages.ShouldHaveSingleItem();
        var text = result.Messages[0].Text!;
        text.ShouldContain("ai:locale=zh-CN");
        text.ShouldContain("ai:tone=formal");
        text.ShouldNotContain("unrelated");
    }

    [Fact]
    public async Task GetContext_NoMatchingClaims_ReturnsEmpty()
    {
        var sut = CreateSut(
            [new Claim("other", "x")],
            new ClaimInjectionOptions { ClaimPrefix = "ai:" });

        var result = await sut.GetContextAsync([]);

        result.ShouldBeSameAs(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContext_NotAuthenticated_ReturnsEmpty()
    {
        var identity = new ClaimsIdentity();  // no auth type = not authenticated
        var user = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var sut = new ClaimContextProvider(accessor, Microsoft.Extensions.Options.Options.Create(new ClaimInjectionOptions()));

        var result = await sut.GetContextAsync([]);

        result.ShouldBeSameAs(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContext_NoHttpContext_ReturnsEmpty()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var sut = new ClaimContextProvider(accessor, Microsoft.Extensions.Options.Options.Create(new ClaimInjectionOptions()));

        var result = await sut.GetContextAsync([]);

        result.ShouldBeSameAs(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContext_IncludedClaimsIncludesNonPrefixedClaims()
    {
        var sut = CreateSut(
            [
                new Claim("ai:locale", "en-US"),
                new Claim("tenant_id", "acme")
            ],
            new ClaimInjectionOptions
            {
                ClaimPrefix = "ai:",
                IncludedClaims = ["tenant_id"]
            });

        var result = await sut.GetContextAsync([]);

        result.Messages.ShouldNotBeNull();
        var text = result.Messages[0].Text!;
        text.ShouldContain("ai:locale=en-US");
        text.ShouldContain("tenant_id=acme");
    }

    [Fact]
    public void TryCreate_AlwaysReturnsSelf()
    {
        var sut = CreateSut([], new ClaimInjectionOptions());
        var context = new ContextProviderCreationContext();

        var provider = sut.TryCreate(context);

        provider.ShouldBeSameAs(sut);
    }

    [Fact]
    public async Task GetContext_UsesTemplateFormat()
    {
        var sut = CreateSut(
            [new Claim("ai:tone", "casual")],
            new ClaimInjectionOptions
            {
                ClaimPrefix = "ai:",
                TemplateFormat = "Override: {claims}"
            });

        var result = await sut.GetContextAsync([]);

        result.Messages.ShouldNotBeNull();
        result.Messages[0].Text.ShouldStartWith("Override:");
    }
}
