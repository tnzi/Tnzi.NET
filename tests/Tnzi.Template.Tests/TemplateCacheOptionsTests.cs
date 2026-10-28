namespace Tnzi.Template.Tests;

/// <summary>
/// <c>Template:CacheExpirationSeconds</c> 与 <c>Template:EnableCache</c> 在引擎里的真实含义。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>0 是校验器放行的值，必须能渲染。</b>此前引擎把它原样交给 <c>ICacheEntry.SlidingExpiration</c>，
/// 而那个属性对 <c>&lt;= 0</c> 的值抛 <see cref="ArgumentOutOfRangeException"/>：应用照常启动，之后每一次渲染
/// （欢迎邮件、二次验证码、票据模板）都失败。0 现在表示「不按时间过期」：条目只在清空缓存或超出条目上限时离开。
/// </para>
/// <para>
/// ★ <b>关掉缓存就是不缓存。</b>此前 <c>EnableCache = false</c> 只跳过编译缓存，文件模板的正文仍按过期时间缓存：
/// 改了磁盘上的模板，一个小时内发出去的仍是旧内容。
/// </para>
/// </remarks>
public class TemplateCacheOptionsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_Template_CacheOptions_{Guid.NewGuid():N}");
    private readonly List<IMemoryCache> _caches = [];

    public TemplateCacheOptionsTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var cache in _caches)
            cache.Dispose();

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private RazorTemplateEngine CreateEngine(bool enableCache, int cacheExpirationSeconds)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _caches.Add(cache);

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = enableCache,
            CacheExpirationSeconds = cacheExpirationSeconds,
            TemplateExtension = ".cshtml",
            EnableHotReload = false
        });

        return new RazorTemplateEngine(options, Mock.Of<ILogger<RazorTemplateEngine>>(), cache);
    }

    [Fact]
    public void Validator_accepts_zero_expiration()
    {
        var errors = new TemplateOptionsValidator().Validate(null, new TemplateOptions { CacheExpirationSeconds = 0 });

        Assert.True(errors.Succeeded);
    }

    [Fact]
    public async Task Zero_expiration_renders_a_string_template()
    {
        var engine = CreateEngine(enableCache: true, cacheExpirationSeconds: 0);

        var result = await engine.RenderAsync("Hello @Model.Name!", new { Name = "World" });

        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public async Task Zero_expiration_renders_a_file_template()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "welcome.cshtml"), "Welcome @Model.Name");
        var engine = CreateEngine(enableCache: true, cacheExpirationSeconds: 0);

        var result = await engine.RenderFromFileAsync("welcome", new { Name = "Ada" });

        Assert.Equal("Welcome Ada", result);
    }

    [Fact]
    public async Task Zero_expiration_keeps_the_compiled_template_cached()
    {
        var engine = CreateEngine(enableCache: true, cacheExpirationSeconds: 0);

        await engine.RenderAsync("Hi @Model.Name", new { Name = "a" });
        var cache = _caches.Single();

        Assert.Equal(1, ((MemoryCache)cache).Count);
    }

    [Fact]
    public async Task Disabled_cache_reads_the_file_template_on_every_render()
    {
        var path = Path.Combine(_tempDir, "notice.cshtml");
        await File.WriteAllTextAsync(path, "Version one");
        var engine = CreateEngine(enableCache: false, cacheExpirationSeconds: 3600);

        var first = await engine.RenderFromFileAsync("notice");
        await File.WriteAllTextAsync(path, "Version two");
        var second = await engine.RenderFromFileAsync("notice");

        Assert.Equal("Version one", first);
        Assert.Equal("Version two", second);
        Assert.Equal(0, ((MemoryCache)_caches.Single()).Count);
    }
}
