namespace Tnzi.Documents.Services;

/// <summary>
/// 按扩展名把转换分给合适的引擎：HTML 交浏览器，其余交 LibreOffice。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是「一种格式只有一个正确答案」：HTML 的判定标准是浏览器长什么样，
/// Office 文档的判定标准是 LibreOffice 打开长什么样，没有哪个引擎两边都对。
/// 而消费方注入的是**一个** <see cref="IDocumentConverter"/>，所以分流必须发生在框架这一侧。
/// </para>
/// <para>
/// <b>顺序即优先级</b>：第一个 <see cref="IDocumentConverter.CanConvert"/> 认领的引擎胜出。
/// ★ <b>刻意不按「谁此刻可用」来挑</b>：那会让同一份 HTML 在装了浏览器的机器上出一份 PDF、
/// 在没装的机器上出另一份**长得完全不同**的 PDF，且两次都返回成功。认领它的引擎不可用时
/// 就该报错 —— 能工作的降级路径比报错危险得多。要显式退回 LibreOffice 的话，
/// 把 <c>Documents:Html:Enabled</c> 设成 false（此时浏览器引擎不再认领 HTML）。
/// </para>
/// </remarks>
public sealed class RoutingDocumentConverter : IDocumentConverter
{
    private readonly IReadOnlyList<IDocumentConverter> _converters;

    /// <summary>初始化一个 <see cref="RoutingDocumentConverter"/> 实例。</summary>
    /// <param name="converters">候选引擎，**按优先级排列**（先认领者胜出）。</param>
    public RoutingDocumentConverter(params IDocumentConverter[] converters)
    {
        Check.NotNullOrEmpty(converters);
        _converters = converters;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 「有没有任何一个引擎能干活」。要问某个具体文件能不能转，用
    /// <see cref="IsAvailableFor"/> —— 只问本属性会在「装了浏览器但没装 LibreOffice」时
    /// 对 <c>.docx</c> 给出肯定答复。
    /// </remarks>
    public bool IsAvailable => _converters.Any(converter => converter.IsAvailable);

    /// <inheritdoc />
    public bool CanConvert(string fileName) => Select(fileName) != null;

    /// <inheritdoc />
    public bool IsAvailableFor(string fileName) => Select(fileName) is { IsAvailable: true };

    /// <inheritdoc />
    public Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(sourceFileName);

        var converter = Select(sourceFileName)
            ?? throw new DocumentConversionException(
                $"'{Path.GetExtension(sourceFileName)}' is not a convertible document format. " +
                $"Supported: {string.Join(", ", DocumentFormats.ConvertibleExtensions.Order(StringComparer.Ordinal))}.");

        return converter.ConvertToPdfAsync(source, sourceFileName, ct);
    }

    private IDocumentConverter? Select(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        return _converters.FirstOrDefault(converter => converter.CanConvert(fileName));
    }
}
