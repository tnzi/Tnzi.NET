using Tnzi.AI.Infrastructure.Tools;

namespace Tnzi.AI.Tests;

/// <summary>
/// OpenApiToolGenerator 单元测试 — 验证从 OpenAPI 3.x 规范自动生成 AITool
/// </summary>
public class OpenApiToolGeneratorTests
{
    #region 测试 OpenAPI JSON 规范

    /// <summary>
    /// 简单 OpenAPI 规范（两个端点）
    /// </summary>
    private const string SimpleOpenApiSpec = """
        {
            "openapi": "3.0.0",
            "info": { "title": "Pet Store", "version": "1.0.0" },
            "servers": [{ "url": "https://api.petstore.io/v1" }],
            "paths": {
                "/pets": {
                    "get": {
                        "operationId": "listPets",
                        "summary": "List all pets",
                        "parameters": [
                            {
                                "name": "limit",
                                "in": "query",
                                "required": false,
                                "description": "Maximum number of pets to return",
                                "schema": { "type": "integer" }
                            }
                        ]
                    },
                    "post": {
                        "operationId": "createPet",
                        "summary": "Create a pet",
                        "requestBody": {
                            "required": true,
                            "content": {
                                "application/json": {
                                    "schema": {
                                        "type": "object",
                                        "required": ["name"],
                                        "properties": {
                                            "name": {
                                                "type": "string",
                                                "description": "Name of the pet"
                                            },
                                            "tag": {
                                                "type": "string",
                                                "description": "Tag for the pet"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                "/pets/{petId}": {
                    "get": {
                        "operationId": "showPetById",
                        "summary": "Info for a specific pet",
                        "parameters": [
                            {
                                "name": "petId",
                                "in": "path",
                                "required": true,
                                "description": "The id of the pet to retrieve",
                                "schema": { "type": "string" }
                            }
                        ]
                    }
                }
            }
        }
        """;

    /// <summary>
    /// 无 operationId 的规范
    /// </summary>
    private const string NoOperationIdSpec = """
        {
            "openapi": "3.0.0",
            "info": { "title": "Test API", "version": "1.0.0" },
            "servers": [{ "url": "https://api.test.io" }],
            "paths": {
                "/users/{userId}/orders": {
                    "get": {
                        "summary": "Get user orders"
                    }
                }
            }
        }
        """;

    /// <summary>
    /// 无 paths 的规范
    /// </summary>
    private const string EmptyPathsSpec = """
        {
            "openapi": "3.0.0",
            "info": { "title": "Empty API", "version": "1.0.0" }
        }
        """;

    #endregion

    #region Helper 方法

    private static OpenApiToolGenerator CreateGenerator()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddOptions<AIOptions>();
        var sp = services.BuildServiceProvider();

        return new OpenApiToolGenerator(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<OpenApiToolGenerator>>());
    }

    private static OpenApiSpec CreateSpec(
        string name = "TestApi",
        List<string>? include = null,
        List<string>? exclude = null)
    {
        return new OpenApiSpec
        {
            Name = name,
            Url = "https://example.com/openapi.json",
            IncludeOperations = include ?? [],
            ExcludeOperations = exclude ?? []
        };
    }

    #endregion

    #region 基本解析测试

    [Fact]
    public void GenerateToolsFromJson_SimpleSpec_CreatesAllTools()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        // 应创建 3 个工具：listPets, createPet, showPetById
        tools.Count.ShouldBe(3);
        tools.ShouldContain(t => t.Name == "TestApi_listPets");
        tools.ShouldContain(t => t.Name == "TestApi_createPet");
        tools.ShouldContain(t => t.Name == "TestApi_showPetById");
    }

    [Fact]
    public void GenerateToolsFromJson_ToolHasDescription()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        var listPets = tools.First(t => t.Name == "TestApi_listPets");
        listPets.Description.ShouldNotBeNullOrWhiteSpace();
        listPets.Description.ShouldContain("List all pets");
    }

    [Fact]
    public void GenerateToolsFromJson_NoOperationId_GeneratesNameFromMethodAndPath()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(NoOperationIdSpec, spec);

        tools.Count.ShouldBe(1);
        // 应该从 GET /users/{userId}/orders 生成名称
        var tool = tools[0];
        tool.Name.ShouldNotBeNullOrWhiteSpace();
        tool.Name.ShouldStartWith("TestApi_");
    }

    [Fact]
    public void GenerateToolsFromJson_EmptyPaths_ReturnsEmpty()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(EmptyPathsSpec, spec);

        tools.Count.ShouldBe(0);
    }

    [Fact]
    public void GenerateToolsFromJson_InvalidJson_ThrowsException()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        Should.Throw<InvalidOperationException>(() =>
            generator.GenerateToolsFromJson("not valid json!", spec));
    }

    #endregion

    #region 参数提取测试

    [Fact]
    public void GenerateToolsFromJson_ExtractsQueryParameters()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        // listPets 应有 limit 查询参数
        var listPets = tools.First(t => t.Name == "TestApi_listPets");
        listPets.ShouldNotBeNull();
    }

    [Fact]
    public void GenerateToolsFromJson_ExtractsPathParameters()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        // showPetById 应有 petId 路径参数
        var showPet = tools.First(t => t.Name == "TestApi_showPetById");
        showPet.ShouldNotBeNull();
    }

    [Fact]
    public void GenerateToolsFromJson_ExtractsRequestBody()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        // createPet 应有 name 和 tag body 参数
        var createPet = tools.First(t => t.Name == "TestApi_createPet");
        createPet.ShouldNotBeNull();
    }

    #endregion

    #region Include/Exclude 过滤测试

    [Fact]
    public void GenerateToolsFromJson_IncludeOperations_OnlyIncluded()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec(include: ["listPets", "showPetById"]);

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        tools.Count.ShouldBe(2);
        tools.ShouldContain(t => t.Name == "TestApi_listPets");
        tools.ShouldContain(t => t.Name == "TestApi_showPetById");
        tools.ShouldNotContain(t => t.Name == "TestApi_createPet");
    }

    [Fact]
    public void GenerateToolsFromJson_ExcludeOperations_ExcludesSpecified()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec(exclude: ["createPet"]);

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        tools.Count.ShouldBe(2);
        tools.ShouldContain(t => t.Name == "TestApi_listPets");
        tools.ShouldContain(t => t.Name == "TestApi_showPetById");
        tools.ShouldNotContain(t => t.Name == "TestApi_createPet");
    }

    [Fact]
    public void GenerateToolsFromJson_IncludeTakesPrecedenceOverExclude()
    {
        var generator = CreateGenerator();
        // 当同时配置 Include 和 Exclude 时，Include 优先
        var spec = CreateSpec(
            include: ["listPets"],
            exclude: ["showPetById"]);

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        tools.Count.ShouldBe(1);
        tools.ShouldContain(t => t.Name == "TestApi_listPets");
    }

    [Fact]
    public void GenerateToolsFromJson_IncludeNonExistentOperationId_ReturnsEmpty()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec(include: ["nonExistentOperation"]);

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        tools.Count.ShouldBe(0);
    }

    #endregion

    #region ShouldIncludeOperation 内部方法测试

    [Fact]
    public void ShouldIncludeOperation_NoFilters_ReturnsTrue()
    {
        var result = OpenApiToolGenerator.ShouldIncludeOperation("anyOp", null, null);
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldIncludeOperation_IncludeContainsId_ReturnsTrue()
    {
        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "listPets" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation("listPets", includeSet, null);
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldIncludeOperation_IncludeDoesNotContainId_ReturnsFalse()
    {
        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "listPets" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation("createPet", includeSet, null);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldIncludeOperation_ExcludeContainsId_ReturnsFalse()
    {
        var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "createPet" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation("createPet", null, excludeSet);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldIncludeOperation_ExcludeDoesNotContainId_ReturnsTrue()
    {
        var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "createPet" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation("listPets", null, excludeSet);
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldIncludeOperation_NullOperationId_NoFilters_ReturnsTrue()
    {
        var result = OpenApiToolGenerator.ShouldIncludeOperation(null, null, null);
        result.ShouldBeTrue();
    }

    [Fact]
    public void ShouldIncludeOperation_NullOperationId_WithInclude_ReturnsFalse()
    {
        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "listPets" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation(null, includeSet, null);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldIncludeOperation_NullOperationId_WithExclude_ReturnsTrue()
    {
        var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "createPet" };
        var result = OpenApiToolGenerator.ShouldIncludeOperation(null, null, excludeSet);
        result.ShouldBeTrue();
    }

    #endregion

    #region 工具名称生成测试

    [Fact]
    public void SanitizeToolName_RemovesSpecialChars()
    {
        var result = OpenApiToolGenerator.SanitizeToolName("get/pets/{id}");
        result.ShouldBe("get_pets_id");
    }

    [Fact]
    public void SanitizeToolName_CollapsesUnderscores()
    {
        var result = OpenApiToolGenerator.SanitizeToolName("get___pets");
        result.ShouldBe("get_pets");
    }

    [Fact]
    public void SanitizeToolName_TrimsUnderscores()
    {
        var result = OpenApiToolGenerator.SanitizeToolName("_pets_");
        result.ShouldBe("pets");
    }

    [Fact]
    public void GenerateToolName_CreatesFromMethodAndPath()
    {
        var result = OpenApiToolGenerator.GenerateToolName("GET", "/users/{userId}/orders");
        result.ShouldBe("get_users_by_userId_orders");
    }

    #endregion

    #region Disabled 模式测试

    [Fact]
    public async Task GenerateToolsAsync_Disabled_ReturnsEmpty()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddOptions<AIOptions>().Configure(o =>
        {
            o.OpenApiTools = new OpenApiToolsOptions { Enabled = false };
        });
        var sp = services.BuildServiceProvider();

        var generator = new OpenApiToolGenerator(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<OpenApiToolGenerator>>());

        var tools = await generator.GenerateToolsAsync();
        tools.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GenerateToolsAsync_EnabledButNoSpecs_ReturnsEmpty()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddOptions<AIOptions>().Configure(o =>
        {
            o.OpenApiTools = new OpenApiToolsOptions { Enabled = true, Specs = [] };
        });
        var sp = services.BuildServiceProvider();

        var generator = new OpenApiToolGenerator(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<OpenApiToolGenerator>>());

        var tools = await generator.GenerateToolsAsync();
        tools.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GenerateToolsAsync_NullOpenApiToolsOptions_ReturnsEmpty()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        // 不设置 OpenApiTools（默认值）
        services.AddOptions<AIOptions>();
        var sp = services.BuildServiceProvider();

        var generator = new OpenApiToolGenerator(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<OpenApiToolGenerator>>());

        var tools = await generator.GenerateToolsAsync();
        tools.Count.ShouldBe(0);
    }

    #endregion

    #region HTTP 调用测试

    [Fact]
    public async Task ExecuteHttpCallAsync_ReplacesPathParameters()
    {
        // 通过 Mock HttpMessageHandler 验证路径参数替换
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            // 验证路径参数被正确替换
            req.RequestUri!.ToString().ShouldContain("/pets/123");
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"id\":\"123\",\"name\":\"Fido\"}")
            });
        });

        var client = new HttpClient(handler);
        var parameters = new List<OpenApiParameter>
        {
            new()
            {
                Name = "petId",
                In = "path",
                Required = true,
                Schema = JsonDocument.Parse("{\"type\":\"string\"}").RootElement.Clone()
            }
        };

        var args = new Dictionary<string, object?> { ["petId"] = "123" };

        var result = await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.petstore.io/v1",
            "/pets/{petId}",
            "GET",
            new Dictionary<string, string>(),
            parameters,
            false,
            args,
            Mock.Of<ILogger>(),
            CancellationToken.None);

        result.ShouldContain("Fido");
    }

    [Fact]
    public async Task ExecuteHttpCallAsync_AddsQueryParameters()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.Query.ShouldContain("limit=10");
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("[]")
            });
        });

        var client = new HttpClient(handler);
        var parameters = new List<OpenApiParameter>
        {
            new()
            {
                Name = "limit",
                In = "query",
                Required = false,
                Schema = JsonDocument.Parse("{\"type\":\"integer\"}").RootElement.Clone()
            }
        };

        var args = new Dictionary<string, object?> { ["limit"] = "10" };

        await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.petstore.io/v1",
            "/pets",
            "GET",
            new Dictionary<string, string>(),
            parameters,
            false,
            args,
            Mock.Of<ILogger>(),
            CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteHttpCallAsync_SendsRequestBody()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"id\":\"1\",\"name\":\"Fido\"}")
            };
        });

        var client = new HttpClient(handler);
        var parameters = new List<OpenApiParameter>
        {
            new()
            {
                Name = "name",
                In = "body",
                Required = true,
                Schema = JsonDocument.Parse("{\"type\":\"string\"}").RootElement.Clone()
            },
            new()
            {
                Name = "tag",
                In = "body",
                Required = false,
                Schema = JsonDocument.Parse("{\"type\":\"string\"}").RootElement.Clone()
            }
        };

        var args = new Dictionary<string, object?> { ["name"] = "Fido", ["tag"] = "dog" };

        await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.petstore.io/v1",
            "/pets",
            "POST",
            new Dictionary<string, string>(),
            parameters,
            true,
            args,
            Mock.Of<ILogger>(),
            CancellationToken.None);

        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("Fido");
        capturedBody.ShouldContain("dog");
    }

    [Fact]
    public async Task ExecuteHttpCallAsync_SetsCustomHeaders()
    {
        string? capturedAuth = null;

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            capturedAuth = req.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{}")
            });
        });

        var client = new HttpClient(handler);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer test-token-123"
        };

        await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.example.com",
            "/data",
            "GET",
            headers,
            [],
            false,
            new Dictionary<string, object?>(),
            Mock.Of<ILogger>(),
            CancellationToken.None);

        capturedAuth.ShouldBe("Bearer test-token-123");
    }

    [Fact]
    public async Task ExecuteHttpCallAsync_HttpError_ReturnsErrorJson()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.NotFound,
                Content = new StringContent("Not Found")
            });
        });

        var client = new HttpClient(handler);

        var result = await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.example.com",
            "/missing",
            "GET",
            new Dictionary<string, string>(),
            [],
            false,
            new Dictionary<string, object?>(),
            Mock.Of<ILogger>(),
            CancellationToken.None);

        result.ShouldContain("\"error\":true");
        result.ShouldContain("404");
    }

    [Fact]
    public async Task ExecuteHttpCallAsync_Exception_ReturnsErrorJson()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var client = new HttpClient(handler);

        var result = await OpenApiToolGenerator.ExecuteHttpCallAsync(
            client,
            "https://api.example.com",
            "/data",
            "GET",
            new Dictionary<string, string>(),
            [],
            false,
            new Dictionary<string, object?>(),
            Mock.Of<ILogger>(),
            CancellationToken.None);

        result.ShouldContain("\"error\":true");
        result.ShouldContain("Connection refused");
    }

    #endregion

    #region 边界情况

    [Fact]
    public void GenerateToolsFromJson_SpecWithServers_ExtractsBaseUrl()
    {
        var specWithServers = """
            {
                "openapi": "3.0.0",
                "info": { "title": "Test", "version": "1.0.0" },
                "servers": [{ "url": "https://api.example.com/v2" }],
                "paths": {
                    "/items": {
                        "get": {
                            "operationId": "listItems",
                            "summary": "List items"
                        }
                    }
                }
            }
            """;

        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(specWithServers, spec);
        tools.Count.ShouldBe(1);
    }

    [Fact]
    public void GenerateToolsFromJson_SkipsNonHttpMethods()
    {
        // OpenAPI paths 可能包含 parameters 等非 HTTP 方法的键
        var specWithParameters = """
            {
                "openapi": "3.0.0",
                "info": { "title": "Test", "version": "1.0.0" },
                "paths": {
                    "/items": {
                        "parameters": [
                            { "name": "X-Request-Id", "in": "header", "schema": { "type": "string" } }
                        ],
                        "get": {
                            "operationId": "listItems",
                            "summary": "List items"
                        }
                    }
                }
            }
            """;

        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(specWithParameters, spec);
        // 应只生成 GET 工具，跳过 parameters 键
        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("TestApi_listItems");
    }

    [Fact]
    public void GenerateToolsFromJson_MultipleSpecNames_PrefixesToolNames()
    {
        var generator = CreateGenerator();
        var spec1 = CreateSpec(name: "PetStore");
        var spec2 = CreateSpec(name: "UserApi");

        var tools1 = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec1);
        var tools2 = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec2);

        tools1.ShouldAllBe(t => t.Name!.StartsWith("PetStore_"));
        tools2.ShouldAllBe(t => t.Name!.StartsWith("UserApi_"));
    }

    [Fact]
    public void GenerateToolsFromJson_HeaderParameters_AreSkipped()
    {
        // header 参数应被忽略（仅处理 path 和 query）
        var specWithHeader = """
            {
                "openapi": "3.0.0",
                "info": { "title": "Test", "version": "1.0.0" },
                "paths": {
                    "/data": {
                        "get": {
                            "operationId": "getData",
                            "summary": "Get data",
                            "parameters": [
                                { "name": "X-Token", "in": "header", "schema": { "type": "string" } },
                                { "name": "format", "in": "query", "schema": { "type": "string" } }
                            ]
                        }
                    }
                }
            }
            """;

        var generator = CreateGenerator();
        var spec = CreateSpec();

        // 不应抛出异常，应成功创建工具
        var tools = generator.GenerateToolsFromJson(specWithHeader, spec);
        tools.Count.ShouldBe(1);
    }

    #endregion

    #region Options 测试

    [Fact]
    public void OpenApiToolsOptions_DefaultValues()
    {
        var options = new OpenApiToolsOptions();
        options.Enabled.ShouldBeFalse();
        options.Specs.ShouldNotBeNull();
        options.Specs.Count.ShouldBe(0);
    }

    [Fact]
    public void OpenApiSpec_DefaultValues()
    {
        var spec = new OpenApiSpec();
        spec.Url.ShouldBe(string.Empty);
        spec.Name.ShouldBe(string.Empty);
        spec.Headers.ShouldNotBeNull();
        spec.Headers.Count.ShouldBe(0);
        spec.IncludeOperations.ShouldNotBeNull();
        spec.IncludeOperations.Count.ShouldBe(0);
        spec.ExcludeOperations.ShouldNotBeNull();
        spec.ExcludeOperations.Count.ShouldBe(0);
    }

    [Fact]
    public void AIOptions_HasOpenApiToolsProperty()
    {
        var options = new AIOptions();
        options.OpenApiTools.ShouldNotBeNull();
        options.OpenApiTools.Enabled.ShouldBeFalse();
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Mock HttpMessageHandler，用于拦截 HTTP 请求
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    #endregion
}
