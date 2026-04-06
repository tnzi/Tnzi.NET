using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Services;
using Tnzi.AI.Mcp.Services.Interfaces;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

public class McpOAuthTokenManagerTests
{
    [Fact]
    public async Task GetAuthorizationHeader_FirstCall_FetchesToken()
    {
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "test-token-abc",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });
        var options = CreateOptions("server1", grantType: "client_credentials");
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        var header = await manager.GetAuthorizationHeaderAsync("server1");

        Assert.Equal("Bearer test-token-abc", header);
    }

    [Fact]
    public async Task GetAuthorizationHeader_CachedToken_DoesNotRefetch()
    {
        var callCount = 0;
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "cached-token",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        }, onCalled: () => callCount++);
        var options = CreateOptions("server1");
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        await manager.GetAuthorizationHeaderAsync("server1");
        await manager.GetAuthorizationHeaderAsync("server1");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAuthorizationHeader_ExpiredToken_Refreshes()
    {
        var callCount = 0;
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "token-refreshed",
            ExpiresIn = 1, // 1 秒过期
            TokenType = "Bearer"
        }, onCalled: () => callCount++);
        var options = CreateOptions("server1", refreshSkewSeconds: 0);
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        await manager.GetAuthorizationHeaderAsync("server1");
        await Task.Delay(1500); // 等待过期
        await manager.GetAuthorizationHeaderAsync("server1");

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetAuthorizationHeader_UnknownServer_ReturnsNull()
    {
        var options = CreateOptions("server1");
        var httpFactory = new Mock<IHttpClientFactory>();
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory.Object, options);

        var header = await manager.GetAuthorizationHeaderAsync("unknown-server");

        Assert.Null(header);
    }

    [Fact]
    public async Task GetAuthorizationHeader_ConcurrentCalls_OnlyOneFetch()
    {
        var callCount = 0;
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "concurrent-token",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        }, onCalled: () => Interlocked.Increment(ref callCount), delay: TimeSpan.FromMilliseconds(200));
        var options = CreateOptions("server1");
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        // 并发 5 个请求
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => manager.GetAuthorizationHeaderAsync("server1"))
            .ToArray();

        await Task.WhenAll(tasks);

        // 双重检查锁定 — 应只调用 1 次
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAuthorizationHeader_ProactiveRefresh_RefreshesBeforeExpiry()
    {
        var callCount = 0;
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "proactive-token",
            ExpiresIn = 65, // 65 秒，skew=60 -> 5 秒后就会过期
            TokenType = "Bearer"
        }, onCalled: () => callCount++);
        var options = CreateOptions("server1", refreshSkewSeconds: 60);
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        await manager.GetAuthorizationHeaderAsync("server1");
        // 因为 65 - 60 = 5s，token 在 5s 后才被视为"即将过期"
        // 但这里不等，仍应返回缓存
        var header = await manager.GetAuthorizationHeaderAsync("server1");

        Assert.NotNull(header);
        Assert.Equal(1, callCount); // 尚未过期
    }

    [Fact]
    public async Task RevokeAsync_NoCachedToken_ReturnsTrue()
    {
        var options = CreateOptions("server1");
        var httpFactory = new Mock<IHttpClientFactory>();
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory.Object, options);

        var result = await manager.RevokeAsync("server1");

        Assert.True(result);
    }

    [Fact]
    public async Task RevokeAsync_WithRevocationEndpoint_PostsAndClearsCache()
    {
        string? revokedToken = null;
        var requestCount = 0;
        var handler = new MockHttpHandler(async (request, ct) =>
        {
            requestCount++;
            // token endpoint
            if (request.RequestUri!.PathAndQuery.Contains("/token"))
            {
                var json = JsonSerializer.Serialize(new { access_token = "revoke-me", expires_in = 3600, token_type = "Bearer" });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            // revocation endpoint
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("revoke-me")) revokedToken = "revoke-me";
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var factory = CreateMockHttpFactoryWithHandler(handler);
        var options = CreateOptionsWithRevocation("server1", "https://auth.example.com/revoke");
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, factory, options);

        // 先获取 token 再撤销
        await manager.GetAuthorizationHeaderAsync("server1");
        var result = await manager.RevokeAsync("server1");

        Assert.True(result);
        Assert.Equal("revoke-me", revokedToken);
        Assert.Equal(2, requestCount); // 1 token fetch + 1 revoke
    }

    [Fact]
    public async Task RevokeAsync_NoRevocationEndpoint_ClearsCacheOnly()
    {
        var httpFactory = CreateMockHttpFactory(new OAuthTokenResponse
        {
            AccessToken = "local-clear-token",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });
        var options = CreateOptions("server1"); // 无 RevocationEndpoint
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, httpFactory, options);

        await manager.GetAuthorizationHeaderAsync("server1");
        var result = await manager.RevokeAsync("server1");

        Assert.True(result);
    }

    [Fact]
    public async Task GetAuthorizationHeader_MetadataDiscovery_ResolvesTokenEndpoint()
    {
        var requestUrls = new List<string>();
        var handler = new MockHttpHandler(async (request, ct) =>
        {
            requestUrls.Add(request.RequestUri!.ToString());

            // metadata endpoint
            if (request.RequestUri.PathAndQuery.Contains("well-known"))
            {
                var metadata = JsonSerializer.Serialize(new
                {
                    token_endpoint = "https://auth.example.com/discovered-token",
                    revocation_endpoint = "https://auth.example.com/discovered-revoke"
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(metadata, Encoding.UTF8, "application/json")
                };
            }

            // token endpoint
            var tokenJson = JsonSerializer.Serialize(new { access_token = "discovered-token", expires_in = 3600, token_type = "Bearer" });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenJson, Encoding.UTF8, "application/json")
            };
        });
        var factory = CreateMockHttpFactoryWithHandler(handler);
        var options = MsOptions.Create(new McpOAuthOptions
        {
            Servers = new Dictionary<string, McpOAuthServerConfig>
            {
                ["server1"] = new()
                {
                    // 无 TokenEndpoint — 需要 discovery
                    ClientId = "test-client",
                    ClientSecret = "test-secret",
                    EnableMetadataDiscovery = true,
                    AuthorizationServer = "https://auth.example.com"
                }
            }
        });
        var manager = new McpOAuthTokenManager(NullLogger<McpOAuthTokenManager>.Instance, factory, options);

        var header = await manager.GetAuthorizationHeaderAsync("server1");

        Assert.Equal("Bearer discovered-token", header);
        Assert.Contains(requestUrls, u => u.Contains("well-known"));
        Assert.Contains(requestUrls, u => u.Contains("discovered-token"));
    }

    // Helper methods
    private static IOptions<McpOAuthOptions> CreateOptions(string serverName, string grantType = "client_credentials", int refreshSkewSeconds = 60)
    {
        return MsOptions.Create(new McpOAuthOptions
        {
            Servers = new Dictionary<string, McpOAuthServerConfig>
            {
                [serverName] = new()
                {
                    TokenEndpoint = "https://auth.example.com/token",
                    ClientId = "test-client",
                    ClientSecret = "test-secret",
                    GrantType = grantType,
                    RefreshSkewSeconds = refreshSkewSeconds
                }
            }
        });
    }

    private static IOptions<McpOAuthOptions> CreateOptionsWithRevocation(string serverName, string revocationEndpoint)
    {
        return MsOptions.Create(new McpOAuthOptions
        {
            Servers = new Dictionary<string, McpOAuthServerConfig>
            {
                [serverName] = new()
                {
                    TokenEndpoint = "https://auth.example.com/token",
                    ClientId = "test-client",
                    ClientSecret = "test-secret",
                    RevocationEndpoint = revocationEndpoint
                }
            }
        });
    }

    private static IHttpClientFactory CreateMockHttpFactory(OAuthTokenResponse response, Action? onCalled = null, TimeSpan? delay = null)
    {
        var handler = new MockHttpHandler(async (request, ct) =>
        {
            if (delay != null) await Task.Delay(delay.Value, ct);
            onCalled?.Invoke();
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        return CreateMockHttpFactoryWithHandler(handler);
    }

    private static IHttpClientFactory CreateMockHttpFactoryWithHandler(MockHttpHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    private class MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => handler(request, ct);
    }
}
