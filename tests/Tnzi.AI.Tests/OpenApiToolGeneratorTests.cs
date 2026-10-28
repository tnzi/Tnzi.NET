
namespace Tnzi.AI.Tests;

/// <summary>
/// OpenApiToolGenerator 单元测试 - 验证从 OpenAPI 3.x 规范自动生成 AITool
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

    // 模型只能从工具的 JSON Schema 得知参数名；schema 是空对象时它只会以 {} 调用，
    // 路径占位符原样留在 URL 里、必填的请求体字段缺失

    private static JsonElement SchemaOf(IReadOnlyList<AITool> tools, string name)
        => tools.OfType<AIFunction>().Single(t => t.Name == name).JsonSchema;

    private static string[] RequiredOf(JsonElement schema)
        => schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : [];

    [Fact]
    public void GenerateToolsFromJson_ExtractsQueryParameters()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        var schema = SchemaOf(tools, "TestApi_listPets");
        schema.GetProperty("type").GetString().ShouldBe("object");
        var limit = schema.GetProperty("properties").GetProperty("limit");
        limit.GetProperty("type").GetString().ShouldBe("integer");
        limit.GetProperty("description").GetString().ShouldBe("Maximum number of pets to return");
        RequiredOf(schema).ShouldBeEmpty();
    }

    [Fact]
    public void GenerateToolsFromJson_ExtractsPathParameters()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        var schema = SchemaOf(tools, "TestApi_showPetById");
        schema.GetProperty("properties").GetProperty("petId").GetProperty("type").GetString().ShouldBe("string");
        RequiredOf(schema).ShouldBe(["petId"]);
    }

    [Fact]
    public void GenerateToolsFromJson_ExtractsRequestBody()
    {
        var generator = CreateGenerator();
        var spec = CreateSpec();

        var tools = generator.GenerateToolsFromJson(SimpleOpenApiSpec, spec);

        var schema = SchemaOf(tools, "TestApi_createPet");
        var properties = schema.GetProperty("properties");
        properties.GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
        properties.GetProperty("tag").GetProperty("description").GetString().ShouldBe("Tag for the pet");
        RequiredOf(schema).ShouldBe(["name"]);
    }

    private const string ComponentRefSpec = """
        {
            "openapi": "3.0.0",
            "info": { "title": "Pet Store", "version": "1.0.0" },
            "servers": [{ "url": "https://api.petstore.io/v1" }],
            "paths": {
                "/pets": {
                    "post": {
                        "operationId": "addPet",
                        "requestBody": {
                            "required": true,
                            "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewPet" } } }
                        }
                    }
                },
                "/pets/batch": {
                    "post": {
                        "operationId": "addPets",
                        "requestBody": {
                            "required": true,
                            "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/NewPet" } } } }
                        }
                    }
                }
            },
            "components": {
                "schemas": {
                    "NewPet": {
                        "type": "object",
                        "required": ["name"],
                        "properties": {
                            "name": { "type": "string" },
                            "owner": { "$ref": "#/components/schemas/Owner" }
                        }
                    },
                    "Owner": {
                        "type": "object",
                        "properties": { "name": { "type": "string" }, "pets": { "type": "array", "items": { "$ref": "#/components/schemas/NewPet" } } }
                    }
                }
            }
        }
        """;

    [Fact]
    public void GenerateToolsFromJson_ComponentRefs_AreInlinedSoTheSchemaIsSelfContained()
    {
        // 工具 schema 是独立文档：留一个指向 #/components 的 $ref，提供商要么拒绝整个请求、要么模型看不到字段
        var tools = CreateGenerator().GenerateToolsFromJson(ComponentRefSpec, CreateSpec());

        var schema = SchemaOf(tools, "TestApi_addPet");
        var properties = schema.GetProperty("properties");
        properties.GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
        properties.GetProperty("owner").GetProperty("properties").GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
        RequiredOf(schema).ShouldBe(["name"]);
        schema.GetRawText().ShouldNotContain("#/components");
    }

    [Fact]
    public void GenerateToolsFromJson_NonObjectRequestBody_IsOneRequiredBodyParameter()
    {
        var tools = CreateGenerator().GenerateToolsFromJson(ComponentRefSpec, CreateSpec());

        var schema = SchemaOf(tools, "TestApi_addPets");
        var body = schema.GetProperty("properties").GetProperty("body");
        body.GetProperty("type").GetString().ShouldBe("array");
        body.GetProperty("items").GetProperty("properties").GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
        RequiredOf(schema).ShouldBe(["body"]);
    }

    private static (OpenApiToolGenerator Generator, List<HttpRequestMessage> Requests, List<string?> Bodies) CreateGeneratorWithRecordingHttp()
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string?>();
        var services = new ServiceCollection();
        services.AddHttpClient("Tnzi.AI.OpenApi").ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler(async (req, ct) =>
        {
            requests.Add(req);
            bodies.Add(req.Content == null ? null : await req.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }));
        services.AddOptions<AIOptions>();
        var sp = services.BuildServiceProvider();
        var generator = new OpenApiToolGenerator(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<OpenApiToolGenerator>>());
        return (generator, requests, bodies);
    }

    [Fact]
    public async Task GeneratedTool_InvokedWithTheSchemaArguments_CallsTheResolvedEndpoint()
    {
        var (generator, requests, _) = CreateGeneratorWithRecordingHttp();
        var tool = generator.GenerateToolsFromJson(SimpleOpenApiSpec, CreateSpec()).OfType<AIFunction>().Single(t => t.Name == "TestApi_showPetById");

        await tool.InvokeAsync(new AIFunctionArguments { ["petId"] = JsonDocument.Parse("\"42\"").RootElement });

        requests.Single().RequestUri!.AbsolutePath.ShouldBe("/v1/pets/42");
    }

    [Fact]
    public async Task GeneratedTool_MissingRequiredPathParameter_ReturnsAnErrorWithoutSendingARequest()
    {
        var (generator, requests, _) = CreateGeneratorWithRecordingHttp();
        var tool = generator.GenerateToolsFromJson(SimpleOpenApiSpec, CreateSpec()).OfType<AIFunction>().Single(t => t.Name == "TestApi_showPetById");

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        requests.ShouldBeEmpty();
        result!.ToString()!.ShouldContain("petId");
    }

    [Fact]
    public async Task GeneratedTool_NonObjectRequestBody_SendsTheValueItselfAsTheBody()
    {
        var (generator, _, bodies) = CreateGeneratorWithRecordingHttp();
        var tool = generator.GenerateToolsFromJson(ComponentRefSpec, CreateSpec()).OfType<AIFunction>().Single(t => t.Name == "TestApi_addPets");

        await tool.InvokeAsync(new AIFunctionArguments { ["body"] = JsonDocument.Parse("[{\"name\":\"Fido\"}]").RootElement });

        JsonDocument.Parse(bodies.Single()!).RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
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

    #region 规范缓存（单例，跨请求）

    /// <summary>
    /// 工具清单在每次 Agent 解析时都要取。缓存此前放在按请求创建的 ToolResolver 里，
    /// 每轮对话在第一个 token 之前都要对每份规范发一次远程 GET。
    /// </summary>
    [Fact]
    public async Task GenerateToolsAsync_RepeatedCalls_FetchTheSpecOnce()
    {
        var fixture = new CachingFixture();

        var first = await fixture.Generator.GenerateToolsAsync();
        var second = await fixture.Generator.GenerateToolsAsync();

        first.Count.ShouldBe(3);
        second.Select(t => t.Name).ShouldBe(first.Select(t => t.Name));
        fixture.Fetches.ShouldBe(1);
    }

    [Fact]
    public async Task GenerateToolsAsync_CacheExpired_RefetchesAndAFailedRefreshKeepsTheLastTools()
    {
        var fixture = new CachingFixture();
        var first = await fixture.Generator.GenerateToolsAsync();

        fixture.Clock.Advance(OpenApiToolGenerator.SpecCacheDuration + TimeSpan.FromSeconds(1));
        fixture.FailFetches = true;
        var duringOutage = await fixture.Generator.GenerateToolsAsync();

        fixture.Fetches.ShouldBe(2);
        duringOutage.Select(t => t.Name).ShouldBe(first.Select(t => t.Name));

        // 故障期间不是每个请求都再去拉一次
        await fixture.Generator.GenerateToolsAsync();
        fixture.Fetches.ShouldBe(2);
    }

    [Fact]
    public async Task GenerateToolsAsync_ConfigurationChanged_Refetches()
    {
        var fixture = new CachingFixture();
        await fixture.Generator.GenerateToolsAsync();

        fixture.Options.Change();
        await fixture.Generator.GenerateToolsAsync();

        fixture.Fetches.ShouldBe(2);
    }

    private sealed class CachingFixture
    {
        public CachingFixture()
        {
            var services = new ServiceCollection();
            services.AddHttpClient("Tnzi.AI.OpenApi").ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler((_, _) =>
            {
                Fetches++;
                return Task.FromResult(FailFetches
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(SimpleOpenApiSpec) });
            }));
            var sp = services.BuildServiceProvider();
            Options = new ChangingOptionsMonitor(new AIOptions
            {
                OpenApiTools = new OpenApiToolsOptions
                {
                    Enabled = true,
                    Specs = [new OpenApiSpec { Name = "TestApi", Url = "https://specs.example/openapi.json" }]
                }
            });
            Generator = new OpenApiToolGenerator(
                sp.GetRequiredService<IHttpClientFactory>(), Options, Mock.Of<ILogger<OpenApiToolGenerator>>(), Clock);
        }

        public OpenApiToolGenerator Generator { get; }
        public ChangingOptionsMonitor Options { get; }
        public ManualClock Clock { get; } = new();
        public int Fetches { get; private set; }
        public bool FailFetches { get; set; }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class ChangingOptionsMonitor(AIOptions value) : IOptionsMonitor<AIOptions>
    {
        private readonly List<Action<AIOptions, string?>> _listeners = [];
        public AIOptions CurrentValue => value;
        public AIOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AIOptions, string?> listener)
        {
            _listeners.Add(listener);
            return null;
        }

        public void Change()
        {
            foreach (var listener in _listeners)
            {
                listener(value, null);
            }
        }
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
