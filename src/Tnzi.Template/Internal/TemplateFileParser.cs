
namespace Tnzi.Template.Internal;

/// <summary>
/// 模板/布局文件解析器（YAML front matter + Razor 内容）
/// </summary>
public class TemplateFileParser
{
    private readonly IDeserializer _deserializer;
    private readonly ILogger<TemplateFileParser>? _logger;

    public TemplateFileParser(ILogger<TemplateFileParser>? logger = null)
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _logger = logger;
    }

    /// <summary>
    /// 解析模板文件（返回 TemplateInfo）
    /// </summary>
    public async Task<TemplateInfo?> ParseTemplateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        var (metadataText, body) = ExtractFrontMatter(content);
        var metadata = ParseTemplateMetadata(metadataText);

        var fileInfo = new FileInfo(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var category = fileInfo.Directory?.Name;

        return new TemplateInfo
        {
            Name = name,
            Category = category,
            SubjectTemplate = metadata.Subject ?? string.Empty,
            ContentTemplate = body,
            DefaultLayoutName = metadata.Layout,
            // 顶层 description 此前解析出来就被丢掉，只有嵌在 metadata: 下的同名键能被读到 ——
            // 于是按文件格式文档写法声明描述的模板，导入后描述是空的
            Description = metadata.Description,
            Metadata = metadata.Metadata ?? new Dictionary<string, object>(),
            FilePath = fileInfo.FullName,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    /// <summary>
    /// 解析布局文件（返回 LayoutInfo）
    /// </summary>
    public async Task<LayoutInfo?> ParseLayoutFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        var (metadataText, body) = ExtractFrontMatter(content);
        var metadata = ParseLayoutMetadata(metadataText);

        var fileInfo = new FileInfo(filePath);
        var rawName = Path.GetFileNameWithoutExtension(filePath);
        var name = rawName.StartsWith("_") ? rawName[1..] : rawName;
        var category = fileInfo.Directory?.Name;

        return new LayoutInfo
        {
            Name = name,
            Category = category,
            LayoutContent = body,
            IsDefault = metadata.IsDefault,
            Metadata = metadata.Metadata ?? new Dictionary<string, object>(),
            FilePath = fileInfo.FullName,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    // front matter 的识别规则统一在 FrontMatterExtractor —— 渲染路径要按同一规则剥离头部，
    // 两边各写一份正则迟早会让"导入后渲染"与"按文件渲染"得到不同的正文
    private static (string? FrontMatter, string Body) ExtractFrontMatter(string content)
        => FrontMatterExtractor.Extract(content);

    private TemplateMetadata ParseTemplateMetadata(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new TemplateMetadata();
        }

        try
        {
            return _deserializer.Deserialize<TemplateMetadata>(yaml) ?? new TemplateMetadata();
        }
        catch (Exception ex)
        {
            // 解析失败返回默认值，记录警告日志
            _logger?.LogWarning(ex, "Failed to parse template metadata YAML. Using default metadata. YAML content: {Yaml}", yaml);
            return new TemplateMetadata();
        }
    }

    private LayoutMetadata ParseLayoutMetadata(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new LayoutMetadata();
        }

        try
        {
            return _deserializer.Deserialize<LayoutMetadata>(yaml) ?? new LayoutMetadata();
        }
        catch (Exception ex)
        {
            // 解析失败返回默认值，记录警告日志
            _logger?.LogWarning(ex, "Failed to parse layout metadata YAML. Using default metadata. YAML content: {Yaml}", yaml);
            return new LayoutMetadata();
        }
    }

    private class TemplateMetadata
    {
        public string? Subject { get; set; }
        public string? Layout { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class LayoutMetadata
    {
        public string? Description { get; set; }
        public string? Type { get; set; }
        public bool IsDefault { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}