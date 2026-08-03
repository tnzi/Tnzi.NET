namespace Tnzi.Documents.Services;

/// <summary>
/// 把 Office 等格式的文档转成 PDF。
/// </summary>
/// <remarks>
/// 与 <c>Tnzi.AI.Infrastructure.Documents.IDocumentConverter</c> 同名但**不是**一回事：
/// 那个是给 RAG 摄取用的「文档转 Markdown 文本」，本接口是「文档转 PDF 字节」，
/// 两者在不同程序集、不同命名空间，各自服务于不同链路。
/// </remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，签名可能随消费方需求调整")]
public interface IDocumentConverter
{
    /// <summary>
    /// 判断该文件名的扩展名是否可转 PDF。
    /// </summary>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    /// <remarks><c>.pdf</c> 返回 false —— PDF 无需转换，调用方直接透传。</remarks>
    bool CanConvert(string fileName);

    /// <summary>
    /// 把源文档转成 PDF。
    /// </summary>
    /// <param name="source">源文档字节。</param>
    /// <param name="sourceFileName">源文件名；**只有扩展名会被采用**（决定临时文件名），其余部分丢弃。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>PDF 字节。</returns>
    /// <exception cref="Exceptions.DocumentConversionException">
    /// 转换器不可用、扩展名不支持、超时、外部进程失败或没有产出。
    /// </exception>
    Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default);
}
