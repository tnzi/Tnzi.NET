namespace Tnzi.Documents.Services;

/// <summary>
/// 把若干份 PDF 按给定顺序拼成一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>只收 PDF。</b>其它格式先经 <see cref="IDocumentConverter"/> 转成 PDF 再来 —— 本契约刻意
/// 不做"顺手转一下"：那会让一个纯字节操作的原语反过来依赖 LibreOffice / 浏览器装没装，
/// 而合并失败与转换失败是两类完全不同的运维问题，混在一个错误里没人分得清是哪一类。
/// </para>
/// <para>
/// <b>合并会丢注释、表单域与数字签名。</b>这不是实现偷懒，而是"把页搬进新文档"这件事本身的语义：
/// 签名覆盖的是原文件的字节，页一旦被搬走，签名要么失效要么无从校验。需要保留签名就不要合并，
/// 改为分别投递。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，签名可能随消费方需求调整")]
public interface IPdfCombiner
{
    /// <summary>
    /// 按 <paramref name="documents"/> 的顺序把每份 PDF 的所有页依次拼进一份新 PDF。
    /// </summary>
    /// <remarks>
    /// 每一页保留原来的尺寸与方向（横竖混排的输入出来仍是横竖混排），本方法不做任何缩放或旋转。
    /// <para>
    /// 只有一份输入时**原样返回传入的字节**：那份文档本来就已经是答案，再经 PDFsharp 转一手
    /// 只会平白改写文件结构（内嵌注释、书签这类东西在重写中会掉）。合法性检查照做，
    /// 所以"一份"与"多份"在什么算合法输入上没有分歧。
    /// </para>
    /// </remarks>
    /// <param name="documents">按投递顺序排列的 PDF 字节；不得为空列表。</param>
    /// <returns>拼好的 PDF 字节。</returns>
    /// <exception cref="ArgumentException"><paramref name="documents"/> 为空列表（零页文档不是合法 PDF）。</exception>
    /// <exception cref="Exceptions.PdfDocumentException">
    /// 其中某份不是合法 PDF、被加密无法读取，或者一页都没有。异常消息里带出是第几份，
    /// 否则调用方拿着"合并失败"四个字无从下手。
    /// </exception>
    byte[] Combine(IReadOnlyList<byte[]> documents);
}
