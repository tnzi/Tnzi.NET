namespace Tnzi.Documents.Services;

/// <summary>
/// 读取 PDF 的结构信息与带坐标的文本定位。只读，不修改文档。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，签名可能随消费方需求调整")]
public interface IPdfInspector
{
    /// <summary>
    /// 取页数与每页尺寸。
    /// </summary>
    /// <param name="pdf">PDF 字节。</param>
    /// <exception cref="Exceptions.PdfDocumentException">字节不是合法 PDF，或文档已加密。</exception>
    PdfInfo GetInfo(byte[] pdf);

    /// <summary>
    /// 按正则在全文档里找标签，并给出归一化坐标。
    /// </summary>
    /// <param name="pdf">PDF 字节。</param>
    /// <param name="pattern">.NET 正则，例如 <c>\{\{([^}]+)\}\}</c>。</param>
    /// <returns>按页码、再按位置排序的命中列表；无命中时为空列表。</returns>
    /// <remarks>
    /// 匹配跑在**字母级拼接**出来的文本上（而不是分词结果），所以形如
    /// <c>{{Key;type=date;role=Client}}</c> 这种会被分词器打散的标签也能整体命中；
    /// 代价是排版空隙不体现在文本里，正则不要依赖空格。
    /// </remarks>
    /// <exception cref="Exceptions.PdfDocumentException">字节不是合法 PDF、文档已加密，或正则非法。</exception>
    IReadOnlyList<PdfTextMatch> FindTags(byte[] pdf, string pattern);
}
