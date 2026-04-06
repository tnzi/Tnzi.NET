using System.Net;
using System.Net.Http;
using System.Text;
using Tnzi.AI.Infrastructure.Mcp;
using Tnzi.AI.Options;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

public class McpOAuthClientHandlerTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<McpOAuthClientHandler>> _logger = new();

    private McpOAuthClientHandler CreateHandler(HttpMessageHandler messageHandler)
    {
        var httpClient = new HttpClient(messageHandler);
        _httpClientFactory
            .Setup(f => f.CreateClient("Tnzi.AI.OAuth"))
            .Returns(httpClient);
        return new McpOAuthClientHandler(_httpClientFactory.Object, _logger.Object);
    }

    #region Config Validation

    [Fact]
    public async Task GetAccessTokenAsync_WithNullConfig_Throws()
    {
        var handler = CreateHandler(new TestMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.GetAccessTokenAsync("test", null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("", "https://token.example.com")]
    [InlineData("client-id", "")]
    public async Task GetAccessTokenAsync_WithMissingRequiredFields_Throws(string clientId, string tokenEndpoint)
    {
        var handler = CreateHandler(new TestMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var config = new McpOAuthConfig
        {
            ClientId = clientId,
            TokenEndpoint = tokenEndpoint
        };

        await Should.ThrowAsync<ArgumentException>(
            () => handler.GetAccessTokenAsync("test", config, CancellationToken.None));
    }

    #endregion

    #region Client Credentials Flow

    [Fact]
    public async Task GetAccessTokenAsync_ClientCredentials_ReturnsToken()
    {
        var tokenResponse = new
        {
            access_token = "test-access-token",
            token_type = "Bearer",
            expires_in = 3600
        };
        var handler = CreateHandler(new TestMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            ClientSecret = "my-secret",
            TokenEndpoint = "https://auth.example.com/token",
            Scopes = ["read", "write"]
        };

        var token = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token.ShouldBe("test-access-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ClientCredentials_SendsCorrectRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var tokenResponse = new
        {
            access_token = "token-123",
            token_type = "Bearer",
            expires_in = 3600
        };
        var handler = CreateHandler(new TestMessageHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            ClientSecret = "my-secret",
            TokenEndpoint = "https://auth.example.com/token",
            Scopes = ["read", "write"]
        };

        await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        capturedRequest.ShouldNotBeNull();
        capturedRequest.Method.ShouldBe(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().ShouldBe("https://auth.example.com/token");
        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("grant_type=client_credentials");
        capturedBody.ShouldContain("client_id=my-client");
        capturedBody.ShouldContain("client_secret=my-secret");
        capturedBody.ShouldContain("scope=read+write");
    }

    [Fact]
    public async Task GetAccessTokenAsync_MetadataDiscovery_UsesDiscoveredTokenEndpoint()
    {
        HttpRequestMessage? tokenRequest = null;
        var handler = CreateHandler(new TestMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                req.RequestUri!.ToString().ShouldBe("https://auth.example.com/.well-known/openid-configuration");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"token_endpoint":"https://auth.example.com/oauth/token","revocation_endpoint":"https://auth.example.com/oauth/revoke"}
                        """, Encoding.UTF8, "application/json")
                };
            }

            tokenRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"access_token":"discovered-token","token_type":"Bearer","expires_in":3600}
                    """, Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            EnableMetadataDiscovery = true,
            AuthorizationServer = "https://auth.example.com"
        };

        var token = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token.ShouldBe("discovered-token");
        tokenRequest.ShouldNotBeNull();
        tokenRequest!.RequestUri!.ToString().ShouldBe("https://auth.example.com/oauth/token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithoutScopes_OmitsScopeParameter()
    {
        string? capturedBody = null;
        var tokenResponse = new { access_token = "token", token_type = "Bearer", expires_in = 3600 };
        var handler = CreateHandler(new TestMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldNotContain("scope");
    }

    #endregion

    #region Token Caching

    [Fact]
    public async Task GetAccessTokenAsync_CachesToken_ReturnsFromCache()
    {
        var callCount = 0;
        var tokenResponse = new { access_token = "cached-token", token_type = "Bearer", expires_in = 3600 };
        var handler = CreateHandler(new TestMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var token1 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);
        var token2 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token1.ShouldBe("cached-token");
        token2.ShouldBe("cached-token");
        callCount.ShouldBe(1); // Only one HTTP call
    }

    [Fact]
    public async Task GetAccessTokenAsync_DifferentServers_CacheSeparately()
    {
        var callCount = 0;
        var handler = CreateHandler(new TestMessageHandler(_ =>
        {
            callCount++;
            var resp = new { access_token = $"token-{callCount}", token_type = "Bearer", expires_in = 3600 };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var token1 = await handler.GetAccessTokenAsync("server-a", config, CancellationToken.None);
        var token2 = await handler.GetAccessTokenAsync("server-b", config, CancellationToken.None);

        token1.ShouldNotBe(token2);
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task InvalidateToken_RemovesCachedToken()
    {
        var callCount = 0;
        var handler = CreateHandler(new TestMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {"access_token":"token-{{callCount}}","token_type":"Bearer","expires_in":3600}
                    """, Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var token1 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);
        handler.InvalidateToken("server1").ShouldBeTrue();
        var token2 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token1.ShouldBe("token-1");
        token2.ShouldBe("token-2");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task RevokeTokenAsync_WithDiscoveredEndpoint_RevokesAndClearsCache()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = CreateHandler(new TestMessageHandler(async req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"token_endpoint":"https://auth.example.com/oauth/token","revocation_endpoint":"https://auth.example.com/oauth/revoke"}
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (req.RequestUri!.ToString().Contains("/oauth/revoke", StringComparison.Ordinal))
            {
                var body = await req.Content!.ReadAsStringAsync();
                body.ShouldContain("token=issued-token");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"access_token":"issued-token","token_type":"Bearer","expires_in":3600}
                    """, Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            ClientSecret = "my-secret",
            EnableMetadataDiscovery = true,
            AuthorizationServer = "https://auth.example.com"
        };

        await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);
        var revoked = await handler.RevokeTokenAsync("server1", config, CancellationToken.None);

        revoked.ShouldBeTrue();
        handler.InvalidateToken("server1").ShouldBeFalse();
        requests.Count(x => x.Method == HttpMethod.Get).ShouldBe(1);
    }

    #endregion

    #region Token Refresh

    [Fact]
    public async Task GetAccessTokenAsync_WithRefreshToken_UsesRefreshFlow()
    {
        var callIndex = 0;
        string? capturedBody = null;

        var handler = CreateHandler(new TestMessageHandler(async req =>
        {
            callIndex++;
            var body = await req.Content!.ReadAsStringAsync();
            if (callIndex == 1)
            {
                // First call: client_credentials, return token with refresh_token and short expiry
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        access_token = "initial-token",
                        token_type = "Bearer",
                        expires_in = 1, // 1 second — will expire immediately with 60s buffer
                        refresh_token = "my-refresh-token"
                    }), Encoding.UTF8, "application/json")
                };
            }
            // Second call: refresh grant
            capturedBody = body;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "refreshed-token",
                    token_type = "Bearer",
                    expires_in = 3600
                }), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            ClientSecret = "my-secret",
            TokenEndpoint = "https://auth.example.com/token"
        };

        // First call — gets initial token (already expired due to 60s buffer)
        var token1 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        // Second call — token expired, should use refresh_token
        var token2 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token1.ShouldBe("initial-token");
        token2.ShouldBe("refreshed-token");
        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("grant_type=refresh_token");
        capturedBody.ShouldContain("refresh_token=my-refresh-token");
    }

    #endregion

    #region Expired Token Re-acquisition

    [Fact]
    public async Task GetAccessTokenAsync_ExpiredTokenWithoutRefresh_ReAcquires()
    {
        var callCount = 0;
        var handler = CreateHandler(new TestMessageHandler(_ =>
        {
            callCount++;
            var resp = new
            {
                access_token = $"token-{callCount}",
                token_type = "Bearer",
                expires_in = callCount == 1 ? 1 : 3600 // First expires immediately (with 60s buffer)
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
            };
        }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var token1 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);
        // Token should be expired due to 60s buffer subtracted from 1s expires_in
        var token2 = await handler.GetAccessTokenAsync("server1", config, CancellationToken.None);

        token1.ShouldBe("token-1");
        token2.ShouldBe("token-2");
        callCount.ShouldBe(2);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task GetAccessTokenAsync_HttpError_ThrowsInvalidOperation()
    {
        var handler = CreateHandler(new TestMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"invalid_client\"}", Encoding.UTF8, "application/json")
            }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.GetAccessTokenAsync("server1", config, CancellationToken.None));
        ex.Message.ShouldContain("server1");
    }

    [Fact]
    public async Task GetAccessTokenAsync_MissingAccessToken_ThrowsInvalidOperation()
    {
        var handler = CreateHandler(new TestMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"token_type\":\"Bearer\"}", Encoding.UTF8, "application/json")
            }));

        var config = new McpOAuthConfig
        {
            ClientId = "my-client",
            TokenEndpoint = "https://auth.example.com/token"
        };

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.GetAccessTokenAsync("server1", config, CancellationToken.None));
        ex.Message.ShouldContain("access_token");
    }

    #endregion

    /// <summary>
    /// Test-only HttpMessageHandler that delegates to a function.
    /// </summary>
    private sealed class TestMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = req => Task.FromResult(handler(req));
        }

        public TestMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
