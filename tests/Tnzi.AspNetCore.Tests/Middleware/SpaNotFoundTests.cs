using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// SPA 深链回落。
///
/// 守的是一条会把「页面不存在」变成「页面一片空白」的线：
/// 旧实现把 404 改写成 200 之后<b>重放同一段管线</b>，
/// 而端点在第一趟就已经选完（选不中即为 null），重放不会重新路由；
/// 下游若没有静态文件中间件，就没有任何人处理这个请求 ——
/// 浏览器拿到 200 和一个空体，看起来像应用坏了，而不是像 404。
/// </summary>
public class SpaNotFoundTests
{
    private sealed class StubFileProvider(string? name, string content) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath)
            => name != null && string.Equals(subpath.TrimStart('/'), name, StringComparison.OrdinalIgnoreCase)
                ? new StubFile(name, content)
                : new NotFoundFileInfo(subpath);

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter)
            => NullChangeToken.Singleton;
    }

    private sealed class StubFile(string name, string content) : IFileInfo
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(content);

        public bool Exists => true;
        public bool IsDirectory => false;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Length => _bytes.Length;
        public string Name => name;
        public string? PhysicalPath => null;
        public Stream CreateReadStream() => new MemoryStream(_bytes);
    }

    private sealed class StubEnvironment(IFileProvider webRoot) : IWebHostEnvironment
    {
        public IFileProvider WebRootFileProvider { get; set; } = webRoot;
        public string WebRootPath { get; set; } = "/wwwroot";
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = webRoot;
        public string ContentRootPath { get; set; } = "/";
        public string EnvironmentName { get; set; } = "Development";
    }

    private const string IndexHtml = "<!doctype html><title>app shell</title>";

    /// <summary>跑一次中间件，返回状态码与真正写出去的响应体。</summary>
    private static async Task<(int StatusCode, string Body)> RunAsync(
        string path,
        int downstreamStatus = StatusCodes.Status404NotFound,
        string? indexFileName = "index.html",
        AspNetCoreOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var wire = new MemoryStream();
        context.Response.Body = wire;

        var environment = new StubEnvironment(new StubFileProvider(indexFileName, IndexHtml));
        var middleware = new SPANotFoundMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = downstreamStatus;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(options ?? new AspNetCoreOptions()),
            environment,
            NullLogger<SPANotFoundMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return (context.Response.StatusCode, Encoding.UTF8.GetString(wire.ToArray()));
    }

    [Fact]
    public async Task ADeepLink_GetsTheAppShell()
    {
        var (status, body) = await RunAsync("/orders/42");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(IndexHtml, body);
    }

    [Fact]
    public async Task ADeepLink_NeverGetsAnEmptyTwoHundred()
    {
        // ★ 这是旧实现的实际产物，也是本次修复的全部理由：200 + 空体在浏览器里是白屏，
        //   比 404 更难诊断。夹具刻意模拟「下游没有静态文件中间件」：管线只答一次 404，
        //   重放时什么都不做。少了这一步，重放会撞回同一个 404，
        //   于是「重放没人处理」和「回落成功」在断言上分不出来（第一版就是这样写的）。
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders/42";
        var wire = new MemoryStream();
        context.Response.Body = wire;

        var passes = 0;
        var middleware = new SPANotFoundMiddleware(
            ctx =>
            {
                if (passes++ == 0)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                }

                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions()),
            new StubEnvironment(new StubFileProvider("index.html", IndexHtml)),
            NullLogger<SPANotFoundMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        var body = Encoding.UTF8.GetString(wire.ToArray());
        Assert.False(
            context.Response.StatusCode == StatusCodes.Status200OK && body.Length == 0,
            "a deep link must never resolve to an empty 200");
        Assert.Equal(IndexHtml, body);
    }

    [Fact]
    public async Task TheAppShell_IsServedUncached()
    {
        // 外壳引用的是带 hash 的分块名。浏览器若缓存了它，发版后刷新拿到的仍是旧外壳，
        // 旧外壳指向的分块已被部署删掉，前端的发版检测与分块恢复刷新多少次都回不到新版本。
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders/42";
        context.Response.Body = new MemoryStream();

        var middleware = new SPANotFoundMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions()),
            new StubEnvironment(new StubFileProvider("index.html", IndexHtml)),
            NullLogger<SPANotFoundMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task WithoutAnAppShell_TheNotFoundStands()
    {
        // wwwroot 里没有 index.html 时，唯一诚实的答案是保留 404。
        var (status, body) = await RunAsync("/orders/42", indexFileName: null);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task ApiRoutes_AreLeftAlone()
    {
        var (status, body) = await RunAsync("/api/orders/42");

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task RequestsForFiles_AreLeftAlone()
    {
        // 带扩展名的是静态资源：缺失的图片必须仍然是 404，
        // 回落成一份 HTML 会让「资源没打包进去」表现为一个能加载但内容不对的文件。
        var (status, _) = await RunAsync("/assets/missing.js");

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ASuccessfulResponse_IsNotTouched()
    {
        var (status, body) = await RunAsync("/orders/42", downstreamStatus: StatusCodes.Status200OK);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task TheIndexPathItself_IsNotRewrittenAgain()
    {
        // 防的是自我回落：/index.html 自己 404 时若再回落一次，就成了死循环的前半段。
        var (status, _) = await RunAsync("/index.html", indexFileName: null);

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ACustomApiPrefix_IsHonoured()
    {
        var options = new AspNetCoreOptions { ApiPathPrefix = "/backend" };

        var (status, _) = await RunAsync("/backend/orders", options: options);

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }
}
