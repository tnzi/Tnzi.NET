using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Finance.Documents.Metadata;
using Tnzi.Template.Services;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 拿一个 <see cref="CheckRenderRequest"/> 跑真正的 Razor 渲染，得到票面 HTML。
/// </summary>
/// <remarks>
/// 模板正文取自 <c>Tnzi.Finance.Documents</c> 的嵌入资源（与启动播种<b>同一份</b>，防两处漂移），
/// 模型经真正的 <c>CheckDocumentModelFactory</c> 构造 —— 也就是打印路径上的那一个。
/// <para>
/// ⚠️ 不经 <c>TemplateCheckRenderer</c> 本身：那一层要的是数据库里的模板存储，
/// 而集成测试夹具的服务闭包里没有它。这里覆盖的是「模型工厂 + 模板正文」这一段，
/// 渲染器自己那一层（按名取模板、异常翻 Result）由 <c>FinanceDocumentsModuleTests</c> 与
/// <c>TemplateCheckRenderer</c> 的契约测试覆盖。
/// </para>
/// </remarks>
internal static class CheckTemplateHarness
{
    /// <summary>按<b>版式名</b>渲染（资源文件名取自出厂声明，测试里不再各写一份映射）。</summary>
    internal static Task<string> RenderByTemplateNameAsync(string templateName, CheckRenderRequest request)
    {
        var builtIn = BuiltInCheckTemplates.Find(templateName);
        builtIn.ShouldNotBeNull($"'{templateName}' is not a built-in layout; this harness renders shipped layouts only.");
        return RenderAsync(builtIn.ResourceFile, request);
    }

    /// <summary>渲染<b>给定的模板正文</b>（不取嵌入资源）——比对"改动前后"的产物时用。</summary>
    internal static Task<string> RenderBodyAsync(string templateBody, CheckRenderRequest request)
        => RenderCoreAsync(templateBody, request);

    internal static Task<string> RenderAsync(string resourceFile, CheckRenderRequest request)
        => RenderCoreAsync(ReadEmbeddedTemplate(resourceFile), request);

    private static async Task<string> RenderCoreAsync(string templateBody, CheckRenderRequest request)
    {
        // OptionsWrapper 而非 Options.Create：本程序集的 global using 引入了 Tnzi.Finance.Options
        // 命名空间，裸 Options.Create 会被解析成命名空间而非 Microsoft.Extensions.Options.Options。
        var engine = new RazorTemplateEngine(
            new OptionsWrapper<TemplateOptions>(new TemplateOptions { EnableCache = false }),
            NullLogger<RazorTemplateEngine>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        var resolution = BuiltInCheckTemplates.Resolve(request.TemplateName, request.Layout);
        return await engine.RenderAsync(templateBody, CheckDocumentModelFactory.Create(request, resolution));
    }

    internal static string ReadEmbeddedTemplate(string resourceFile)
    {
        var assembly = typeof(CheckTemplates).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("Templates." + resourceFile, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
