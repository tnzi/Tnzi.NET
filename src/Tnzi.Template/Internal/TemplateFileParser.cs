
namespace Tnzi.Template.Internal;

/// <summary>
/// 模板/布局文件解析器（YAML front matter + Razor 内容）
/// </summary>
public class TemplateFileParser
{
    private const string FrontMatterDelimiter = "---";
    private static readonly Regex FrontMatterRegex = new(@"^---\s*(.*?)\s*---\s*(.*)$", RegexOptions.Singleline | RegexOptions.Compiled);
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

    private static (string? FrontMatter, string Body) ExtractFrontMatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, string.Empty);
        }

        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            return (null, content);
        }

        var frontMatter = match.Groups[1].Value.Trim();
        var body = match.Groups[2].Value;
        return (frontMatter, body);
    }

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