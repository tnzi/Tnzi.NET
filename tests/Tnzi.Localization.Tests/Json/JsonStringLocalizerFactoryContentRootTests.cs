using System.Globalization;
using Microsoft.Extensions.FileProviders;

namespace Tnzi.Localization.Tests.Json;

/// <summary>
/// 相对的 <c>ResourcesPath</c>（默认 <c>"Resources"</c>）此前按进程当前目录解析。Windows 服务的当前目录是
/// <c>C:\Windows\System32</c>、显式 <c>--contentRoot</c> 的宿主也与当前目录不同：配置照常加载，
/// 而每个 JSON 译文安静地退化成键名本身。现在相对路径按内容根解析，与 appsettings.json 同一个基准。
/// </summary>
public sealed class JsonStringLocalizerFactoryContentRootTests : IDisposable
{
    private readonly string _contentRoot;

    public JsonStringLocalizerFactoryContentRootTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "tnzi-l10n-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "Resources"));
        File.WriteAllText(Path.Combine(_contentRoot, "Resources", "en.json"), """{ "Greeting": "Hello" }""");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让测试失败
        }
    }

    private static JsonStringLocalizerFactory Factory(string resourcesPath, IHostEnvironment? environment)
        => new(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Localization.LocalizationOptions { ResourcesPath = resourcesPath }),
            NullLoggerFactory.Instance,
            hostEnvironment: environment);

    private static string Translate(JsonStringLocalizerFactory factory, string key)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            return factory.Create("SharedResource", "any")[key].Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void ARelativeResourcesPath_ResolvesAgainstTheContentRoot_NotTheCurrentDirectory()
    {
        // 测试进程的当前目录里没有这个临时 Resources：按当前目录解析就只能拿到键名
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "Resources", "en.json"))
                     && File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Resources", "en.json")).Contains("Hello"));

        var factory = Factory("Resources", new StubEnvironment(_contentRoot));

        Assert.Equal("Hello", Translate(factory, "Greeting"));
    }

    [Fact]
    public void AnAbsoluteResourcesPath_IsUsedUnchanged()
    {
        var factory = Factory(Path.Combine(_contentRoot, "Resources"), new StubEnvironment(Path.GetTempPath()));

        Assert.Equal("Hello", Translate(factory, "Greeting"));
    }

    private sealed class StubEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
