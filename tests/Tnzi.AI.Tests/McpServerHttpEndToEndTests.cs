using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Tnzi.AI.Infrastructure.Mcp;
using Tnzi.Modules;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp;
using Tnzi.AI.Mcp.Options;

namespace Tnzi.AI.Tests;

public class McpServerHttpEndToEndTests
{
    [Fact]
    public async Task Endpoint_WithoutApiKey_ReturnsUnauthorized()
    {
        var app = await CreateAppAsync();

        try
        {
            using var client = CreateHttpClient(app);
            var response = await client.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = "1",
                method = "tools/list",
                @params = new { }
            });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_WithApiKey_ListsAndCallsExposedTool()
    {
        var app = await CreateAppAsync();

        try
        {
            var host = app.Services.GetRequiredService<IMcpServerHost>();
            host.ExposeTool(
                "echo",
                "Echoes the provided message",
                payload => Task.FromResult($"echo:{payload.GetProperty("message").GetString()}"));

            await using var factory = new McpClientFactory(NullLoggerFactory.Instance, NullLogger<McpClientFactory>.Instance);
            var adapter = await factory.GetOrCreateClientAsync(new McpServerConfig
            {
                Name = "local-http",
                ConnectionType = McpConnectionType.Http,
                Endpoint = new Uri(GetBaseAddress(app), "/mcp").ToString(),
                Headers = new Dictionary<string, string>
                {
                    [McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret",
                    [McpServerSecurityMiddleware.TenantHeaderName] = "tenant-a"
                }
            });

            var tools = await adapter.ListToolsAsync(CancellationToken.None);
            tools.Select(x => x.Name).ShouldContain("echo");

            var tool = tools.Single(x => x.Name == "echo");
            var response = await InvokeToolTextAsync(tool, new Dictionary<string, object?>
            {
                ["message"] = "hello"
            });

            response.ShouldBe("echo:hello");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:Enabled"] = "true",
            ["AI:Providers:Test:ApiKey"] = "test-key",
            ["AI:Providers:Test:DefaultModel"] = "test-model",
            ["AI:Providers:DeepSeek:Enabled"] = "false",
            ["AI:McpServer:Enabled"] = "true",
            ["AI:McpServer:Endpoint"] = "/mcp",
            ["AI:McpServer:RequireAuthentication"] = "true",
            ["AI:McpServer:AllowedApiKeys:0"] = "secret",
            ["AI:McpServer:EnableAuditLog"] = "false",
            ["AI:McpServer:RateLimitPerMinute"] = "0",
            // McpClientFactory uses a query-string fallback when the installed ModelContextProtocol
            // SDK version does not support custom request headers (see TryApplyHttpQueryFallback).
            // Server-side query-string auth is OFF by default for security; enable it here to
            // exercise the end-to-end fallback path against the current SDK.
            ["AI:McpServer:AllowApiKeyInQuery"] = "true"
        });

        var app = await TnziApp.CreateAsync<TestAiMcpStartupModule>(builder);
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateHttpClient(WebApplication app)
    {
        return new HttpClient
        {
            BaseAddress = GetBaseAddress(app)
        };
    }

    private static Uri GetBaseAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.SingleOrDefault();
        address.ShouldNotBeNull();
        return new Uri(address!);
    }

    private static async Task<string> InvokeToolTextAsync(object tool, Dictionary<string, object?> arguments)
    {
        var method = ResolveInvokeAsyncMethod(tool.GetType());
        var parameters = method.GetParameters();
        var invokeArguments = new List<object?>();
        if (parameters.Length > 0 && parameters[0].ParameterType != typeof(CancellationToken))
        {
            invokeArguments.Add(BuildInvokePayload(parameters[0].ParameterType, arguments));
        }
        if (parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken))
        {
            invokeArguments.Add(CancellationToken.None);
        }

        var invokedTask = method.Invoke(tool, invokeArguments.ToArray());
        if (invokedTask is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return string.Empty;
        }
        if (invokedTask is { } valueTaskLike
            && valueTaskLike.GetType().IsValueType
            && valueTaskLike.GetType().IsGenericType
            && valueTaskLike.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTaskMethod = valueTaskLike.GetType().GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public);
            asTaskMethod.ShouldNotBeNull();
            var genericTask = asTaskMethod!.Invoke(valueTaskLike, null) as Task;
            if (genericTask == null)
            {
                throw new InvalidOperationException("ValueTask tool invocation did not return a Task.");
            }
            await genericTask!.ConfigureAwait(false);
            var genericResult = genericTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(genericTask);
            if (genericResult is string genericText)
            {
                return genericText;
            }
            if (genericResult != null)
            {
                var textProperty = genericResult.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty?.GetValue(genericResult) is string resultText)
                {
                    return resultText;
                }
            }
        }

        var task = invokedTask as Task;
        if (task == null)
        {
            throw new InvalidOperationException("Tool invocation did not return a Task.");
        }
        await task!.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        var result = resultProperty?.GetValue(task);
        if (result is string text)
        {
            return text;
        }

        if (result != null)
        {
            var textProperty = result.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
            if (textProperty?.GetValue(result) is string resultText)
            {
                return resultText;
            }
        }

        throw new InvalidOperationException($"Tool '{tool.GetType().FullName}' did not produce a text result.");
    }

    private static MethodInfo ResolveInvokeAsyncMethod(Type toolType)
    {
        var methods = toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => string.Equals(method.Name, "InvokeAsync", StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length <= 2
                    && (parameters.Length == 0 || parameters[^1].ParameterType == typeof(CancellationToken) || parameters.Length == 1);
            })
            .ToList();

        methods.Count.ShouldBeGreaterThan(0);
        return methods
            .OrderByDescending(method => method.GetParameters().Length)
            .First();
    }

    private static object BuildInvokePayload(Type payloadType, Dictionary<string, object?> arguments)
    {
        if (payloadType == typeof(AIFunctionArguments))
        {
            return new AIFunctionArguments(arguments);
        }

        if (payloadType == typeof(JsonElement))
        {
            return JsonSerializer.SerializeToElement(arguments);
        }

        if (payloadType == typeof(string))
        {
            return JsonSerializer.Serialize(arguments);
        }

        if (typeof(IDictionary<string, object?>).IsAssignableFrom(payloadType)
            || typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(payloadType))
        {
            return arguments;
        }

        if (typeof(IDictionary<string, JsonElement>).IsAssignableFrom(payloadType)
            || typeof(IReadOnlyDictionary<string, JsonElement>).IsAssignableFrom(payloadType))
        {
            return arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => JsonSerializer.SerializeToElement(kvp.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.SerializeToElement(arguments);
    }
}

[DependsOn(typeof(AIMcpModule))]
internal sealed class TestAiMcpStartupModule : TnziApplicationModule
{
}
