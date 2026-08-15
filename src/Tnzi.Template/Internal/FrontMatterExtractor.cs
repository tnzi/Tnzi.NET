namespace Tnzi.Template.Internal;

/// <summary>
/// YAML front matter 提取器 —— 模板/布局文件格式中开头 <c>--- ... ---</c> 块的唯一识别规则。
/// </summary>
/// <remarks>
/// 这个规则必须由**所有**读取模板文件的路径共用：
/// <list type="bullet">
/// <item><see cref="TemplateFileParser"/>（导入到模板存储时解析元数据）</item>
/// <item><c>RazorTemplateEngine</c> 的文件渲染路径（渲染前剥离头部）</item>
/// </list>
/// 两条路径若各有一套判定，同一个文件在"导入后渲染"与"直接按文件渲染"下就会得到不同的正文 ——
/// 而这正是本类被抽出来的原因：引擎此前完全不认识 front matter，把整块 YAML 连同分隔符
/// 一起交给 Razor 编译执行，头部既出现在页面上，其中的 <c>@</c> 还会被当作 Razor 表达式求值。
/// </remarks>
internal static class FrontMatterExtractor
{
    private static readonly Regex FrontMatterRegex = new(@"^---\s*(.*?)\s*---\s*(.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// 拆分文件内容为（front matter 文本，正文）。没有 front matter 时原样返回内容。
    /// </summary>
    public static (string? FrontMatter, string Body) Extract(string? content)
    {
        // 空白内容不做任何加工：调用方（引擎）此前对纯空白文件的行为是原样返回，此处保持不变
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, content ?? string.Empty);
        }

        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            return (null, content);
        }

        return (match.Groups[1].Value.Trim(), match.Groups[2].Value);
    }

    /// <summary>
    /// 去掉 front matter 只保留正文（渲染路径使用；不解析 YAML）。
    /// </summary>
    public static string StripFrontMatter(string? content) => Extract(content).Body;
}
