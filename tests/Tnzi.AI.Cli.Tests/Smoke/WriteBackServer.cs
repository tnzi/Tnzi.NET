using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Tnzi.AI.Mcp;
using Tnzi.AI.Mcp.Server;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// The framework's MCP server, hosted in-process on a loopback port for the write-back smoke test.
/// </summary>
/// <remarks>
/// It accepts one fixed run-scoped credential through the same <see cref="IRunScopedCredentialValidator"/>
/// seam the real one plugs into, so the CLI authenticates exactly as a dispatched run does (a bearer token
/// from its MCP config). Issuing and expiring real credentials is covered by <c>CliRunTokenServiceTests</c>;
/// what only a real CLI can show is that it connects with the composed entry and calls a tool.
/// </remarks>
internal sealed class WriteBackServer : IAsyncDisposable
{
    public const string Token = "tnzi-run_smoke-write-back";
    public static readonly Guid RunId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    public static readonly Guid AgentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
    public static readonly Guid CreatorId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003");
    public static readonly Guid ThreadId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000004");

    /// <summary>
    /// The calling surface of the fixed credential: every consumer tool a smoke test exposes has to be listed here,
    /// or the server leaves it out of <c>tools/list</c> and the model can only write the call out as text.
    /// </summary>
    public static readonly string[] AllowedToolNames =
        ["relay_command", "slow_scan", "take_screenshot", "hand_over_file", "platform_ping", "user_ping", "imposter_ping"];

    private readonly WebApplication _app;

    private WriteBackServer(WebApplication app, string endpoint)
    {
        _app = app;
        Endpoint = endpoint;
    }

    /// <summary>Absolute URL of the MCP endpoint, as <c>AI:Cli:WriteBack:McpEndpoint</c> would hold it.</summary>
    public string Endpoint { get; }

    /// <summary>Where consumer tools are registered.</summary>
    public IMcpServerHost Host => _app.Services.GetRequiredService<IMcpServerHost>();

    /// <param name="holdPostResponses">
    /// Send each POST response, headers included, only once the request has been handled, the way a buffering reverse
    /// proxy in front of the endpoint does. Served directly, the response headers of a tool call go out at once and the
    /// CLI's per-request wait never comes into play.
    /// </param>
    public static async Task<WriteBackServer> StartAsync(bool holdPostResponses = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        if (holdPostResponses)
        {
            builder.Services.AddSingleton<IStartupFilter, HoldPostResponsesStartupFilter>();
        }

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
            // A deployment that uses write-back keeps authentication on; the static key exists only
            // because the validator requires one. The CLI never sees it.
            ["AI:McpServer:RequireAuthentication"] = "true",
            ["AI:McpServer:AllowedApiKeys:0"] = "static-key-the-cli-never-sees",
            ["AI:McpServer:EnableAuditLog"] = "false",
            ["AI:McpServer:EnableToolAnalytics"] = "false",
            ["AI:McpServer:RateLimitPerMinute"] = "0"
        });

        var app = await TnziApp.CreateAsync<WriteBackSmokeStartupModule>(builder);
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new WriteBackServer(app, new Uri(new Uri(address), "/mcp").ToString());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Puts <see cref="HoldPostResponsesMiddleware"/> in front of the whole pipeline.</summary>
internal sealed class HoldPostResponsesStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<HoldPostResponsesMiddleware>();
            next(app);
        };
}

/// <summary>
/// Writes a POST response to memory and sends it to the client only after the rest of the pipeline has finished.
/// </summary>
internal sealed class HoldPostResponsesMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        var connection = context.Features.Get<IHttpResponseBodyFeature>()!;
        using var buffer = new MemoryStream();
        var held = new HeldResponseBody(buffer);
        context.Features.Set<IHttpResponseBodyFeature>(held);
        try
        {
            await next(context);
            await held.CompleteAsync();
        }
        finally
        {
            context.Features.Set(connection);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(connection.Stream, context.RequestAborted);
    }

    /// <summary>A response body that never starts the response: headers stay unsent until the buffer is copied out.</summary>
    private sealed class HeldResponseBody(MemoryStream buffer) : IHttpResponseBodyFeature
    {
        private PipeWriter? _writer;

        public Stream Stream => buffer;

        public PipeWriter Writer => _writer ??= PipeWriter.Create(buffer, new StreamPipeWriterOptions(leaveOpen: true));

        public void DisableBuffering()
        {
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task CompleteAsync()
        {
            if (_writer is not null)
            {
                await _writer.FlushAsync();
            }
        }
    }
}

[DependsOn(typeof(AIMcpModule))]
internal sealed class WriteBackSmokeStartupModule : TnziApplicationModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IRunScopedCredentialValidator, FixedRunCredentialValidator>();
        return Task.CompletedTask;
    }
}

internal sealed class FixedRunCredentialValidator : IRunScopedCredentialValidator
{
    public Task<RunScopedCredential?> ValidateAsync(string token, CancellationToken cancellationToken = default)
        => Task.FromResult(token == WriteBackServer.Token
            ? new RunScopedCredential
            {
                RunId = WriteBackServer.RunId,
                AgentId = WriteBackServer.AgentId,
                CreatorId = WriteBackServer.CreatorId,
                ThreadId = WriteBackServer.ThreadId,
                AllowedToolNames = WriteBackServer.AllowedToolNames
            }
            : null);
}
