using System.Net;
using System.Net.Sockets;
using Tnzi.AI.Sandbox;
using Tnzi.AI.Sandbox.Events.Handlers;
using Tnzi.AI.Sandbox.Quota;
using Tnzi.AI.Sandbox.Services;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 线程删除带走沙箱里的线程目录与配额计数；删除不跟进 agent 建的链接。
/// 另外两条模块注册事实：Docker 客户端的地址取绑定后的选项，基址不带一个并不生效的 API 版本。
/// </summary>
public class SandboxThreadCleanupTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;
    private readonly Guid _threadId = Guid.NewGuid();

    public SandboxThreadCleanupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"tnzi-sandbox-cleanup-{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"tnzi-sandbox-cleanup-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        // 收尾同样不能跟进链接：用例在线程目录里建了指向 _outside 的链接，还留着只读文件
        foreach (var dir in new[] { _root, _outside })
        {
            try { SandboxFileSystem.DeleteTree(dir); } catch { /* best effort */ }
        }
    }

    private VirtualPathTranslator Translator => new(_root);

    private string SeedThreadDirectory()
    {
        var threadDir = Translator.GetThreadDirectory(_threadId);
        foreach (var sub in new[] { "workspace", "uploads", "outputs", "skills/pdf" })
            Directory.CreateDirectory(Path.Combine(threadDir, sub));
        File.WriteAllText(Path.Combine(threadDir, "uploads", "contract.pdf"), "contract");
        File.WriteAllText(Path.Combine(threadDir, "outputs", "report.csv"), "a,b");
        File.WriteAllText(Path.Combine(threadDir, "skills", "pdf", "run.py"), "print(1)");
        File.WriteAllText(Path.Combine(threadDir, ".skills_wired"), "copied");
        // git 对象是只读的：Windows 上不先去掉只读属性就删不掉
        var readOnly = Path.Combine(threadDir, "workspace", "pack.idx");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        return threadDir;
    }

    [Fact]
    public async Task ThreadDeleted_RemovesTheThreadDirectory_AndResetsTheQuota()
    {
        var threadDir = SeedThreadDirectory();
        var quota = new Mock<IThreadResourceQuota>();
        var handler = new SandboxThreadCleanupHandler(Translator, NullLogger<SandboxThreadCleanupHandler>.Instance, quota.Object);

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = _threadId });

        Directory.Exists(threadDir).ShouldBeFalse();
        Directory.Exists(_root).ShouldBeTrue();
        quota.Verify(q => q.ResetAsync(_threadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThreadDeleted_Twice_IsANoOpTheSecondTime()
    {
        SeedThreadDirectory();
        var handler = new SandboxThreadCleanupHandler(Translator, NullLogger<SandboxThreadCleanupHandler>.Instance);

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = _threadId });
        await Should.NotThrowAsync(() => handler.HandleAsync(new ThreadDeletedEvent { ThreadId = _threadId }));
    }

    [Fact]
    public async Task ThreadDeleted_LeavesOtherThreadsAlone()
    {
        SeedThreadDirectory();
        var other = Translator.GetThreadDirectory(Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(other, "workspace"));
        File.WriteAllText(Path.Combine(other, "workspace", "keep.txt"), "keep");
        var handler = new SandboxThreadCleanupHandler(Translator, NullLogger<SandboxThreadCleanupHandler>.Instance);

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = _threadId });

        File.Exists(Path.Combine(other, "workspace", "keep.txt")).ShouldBeTrue();
    }

    [SymlinkFact]
    public async Task ThreadDeleted_RemovesLinksWithoutTouchingWhatTheyPointAt()
    {
        var threadDir = SeedThreadDirectory();
        var secret = Path.Combine(_outside, "secret.txt");
        File.WriteAllText(secret, "host file");
        Directory.CreateSymbolicLink(Path.Combine(threadDir, "workspace", "escape"), _outside);
        File.CreateSymbolicLink(Path.Combine(threadDir, "outputs", "leak.txt"), secret);

        var handler = new SandboxThreadCleanupHandler(Translator, NullLogger<SandboxThreadCleanupHandler>.Instance);

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = _threadId });

        Directory.Exists(threadDir).ShouldBeFalse();
        File.ReadAllText(secret).ShouldBe("host file");
    }

    [Fact]
    public void SandboxModule_RegistersTheThreadCleanupHandler()
    {
        var services = ConfigureSandboxModule(new Dictionary<string, string?>());

        services.ShouldContain(d => d.ServiceType == typeof(IEventHandler<ThreadDeletedEvent>)
            && d.ImplementationType == typeof(SandboxThreadCleanupHandler));
    }

    [Fact]
    public async Task DockerClient_ConnectsToTheConfiguredHost_FromBoundOptions_WithAnUnversionedBaseAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var services = ConfigureSandboxModule(new Dictionary<string, string?> { ["AI:Sandbox:Provider"] = "docker" });
        // 用代码配置：此前只读原始配置键，代码方式设置的 DockerHost 不生效
        services.Configure<SandboxModuleOptions>(o => o.Docker.DockerHost = $"tcp://127.0.0.1:{port}");
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(DockerSandboxProvider.HttpClientName);
        client.BaseAddress!.AbsolutePath.ShouldBe("/");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accept = listener.AcceptTcpClientAsync(cts.Token).AsTask();
        var request = client.GetAsync("/_ping", cts.Token);

        using var accepted = await accept;
        using var reader = new StreamReader(accepted.GetStream());
        var requestLine = await reader.ReadLineAsync(cts.Token);
        requestLine.ShouldBe("GET /_ping HTTP/1.1");

        accepted.Close();
        await Should.ThrowAsync<Exception>(() => request);
    }

    private ServiceCollection ConfigureSandboxModule(Dictionary<string, string?> settings)
    {
        settings["AI:Sandbox:DataRoot"] = _root;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new SandboxTestSupport.TestHostEnvironment { ContentRootPath = Path.GetTempPath() });
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, config);
        var module = new AISandboxModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }
}
