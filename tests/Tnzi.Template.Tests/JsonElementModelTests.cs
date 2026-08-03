namespace Tnzi.Template.Tests;

/// <summary>
/// 模板变量经 HTTP 传入时的类型归一测试。
///
/// 回归背景：通知/模板的入参是 <c>Dictionary&lt;string, object&gt;</c>，经 JSON 反序列化后
/// 每个值都是 <see cref="JsonElement"/>。<c>SafeDynamicObject</c> 早先原样透传，模板里
/// 只有纯输出（走 ToString）能用，任何类型化运算都会在渲染期抛异常：
/// <c>@Model.Flag == true</c> 抛 "Operator '==' cannot be applied to operands of type
/// 'JsonElement' and 'bool'"，<c>string.IsNullOrEmpty(@Model.Url)</c> 抛无匹配重载。
/// 于是框架内置的 WelcomeEmail / EmailConfirmation（两者都含条件分支）经管理端 API
/// 发送必然 500，而进程内事件处理器（传原生 C# 值）却是好的 —— 两条路径对模板不等价。
/// </summary>
public class JsonElementModelTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly string _tempDir;

    public JsonElementModelTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_JsonElement_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = false,
            TemplateExtension = ".cshtml"
        });

        _engine = new RazorTemplateEngine(options, new Mock<ILogger<RazorTemplateEngine>>().Object, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>模拟经 API 传入的模板变量（值全是 JsonElement）</summary>
    private static Dictionary<string, object> FromJson(string json)
        => JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

    [Fact]
    public async Task BooleanValue_SupportsEqualityComparison()
    {
        var model = FromJson("""{"RequireEmailConfirmation": true}""");

        var result = await _engine.RenderAsync(
            "@if (Model.RequireEmailConfirmation == true) { <text>CONFIRM</text> } else { <text>WELCOME</text> }",
            model);

        Assert.Contains("CONFIRM", result);
    }

    [Fact]
    public async Task FalseBooleanValue_TakesElseBranch()
    {
        var model = FromJson("""{"RequireEmailConfirmation": false}""");

        var result = await _engine.RenderAsync(
            "@if (Model.RequireEmailConfirmation == true) { <text>CONFIRM</text> } else { <text>WELCOME</text> }",
            model);

        Assert.Contains("WELCOME", result);
    }

    [Fact]
    public async Task StringValue_WorksWithStringIsNullOrEmpty()
    {
        var model = FromJson("""{"ConfirmationUrl": "https://example.com/confirm"}""");

        var result = await _engine.RenderAsync(
            "@if (!string.IsNullOrEmpty(Model.ConfirmationUrl)) { <text>@Model.ConfirmationUrl</text> }",
            model);

        Assert.Contains("https://example.com/confirm", result);
    }

    [Fact]
    public async Task MissingStringValue_IsTreatedAsEmpty()
    {
        var model = FromJson("""{"ConfirmationUrl": null}""");

        var result = await _engine.RenderAsync(
            "@if (string.IsNullOrEmpty(Model.ConfirmationUrl)) { <text>EMPTY</text> }",
            model);

        Assert.Contains("EMPTY", result);
    }

    [Fact]
    public async Task IntegerValue_KeepsIntegerShapeForComparison()
    {
        var model = FromJson("""{"ExpirationMinutes": 30}""");

        var result = await _engine.RenderAsync(
            "@if (Model.ExpirationMinutes > 10) { <text>@Model.ExpirationMinutes minutes</text> }",
            model);

        Assert.Contains("30 minutes", result);
    }

    [Fact]
    public async Task NestedObject_IsAccessibleAsProperties()
    {
        var model = FromJson("""{"User": {"Name": "Tan", "IsAdmin": true}}""");

        var result = await _engine.RenderAsync(
            "@if (Model.User.IsAdmin == true) { <text>@Model.User.Name</text> }",
            model);

        Assert.Contains("Tan", result);
    }

    [Fact]
    public async Task Array_IsEnumerable()
    {
        var model = FromJson("""{"Items": ["a", "b", "c"]}""");

        var result = await _engine.RenderAsync(
            "@foreach (var item in Model.Items) { <text>@item</text> }",
            model);

        Assert.Contains("a", result);
        Assert.Contains("c", result);
    }

    /// <summary>
    /// 归一后的值必须是 CLR 原生类型，而不是 JsonElement —— 直接锁定转换本身，
    /// 避免将来有人在 ConvertValue 里"顺手"退回透传。
    /// </summary>
    [Fact]
    public void ConvertedValues_AreClrPrimitives()
    {
        var source = FromJson("""{"Flag": true, "Name": "x", "Count": 7, "Ratio": 1.5, "Nothing": null}""");

        var model = SafeDynamicObject.FromDictionary(source);
        var members = model.GetMembers();

        Assert.Equal(true, members["Flag"]);
        Assert.Equal("x", members["Name"]);
        Assert.Equal(7, members["Count"]);
        Assert.Equal(1.5m, members["Ratio"]);
        Assert.Null(members["Nothing"]);
    }
}
