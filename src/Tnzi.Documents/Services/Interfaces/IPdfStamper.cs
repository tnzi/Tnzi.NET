namespace Tnzi.Documents.Services;

/// <summary>
/// 按归一化坐标往 PDF 上画东西，并输出压平后的 PDF。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，签名可能随消费方需求调整")]
public interface IPdfStamper
{
    /// <summary>
    /// 执行一次盖章：先追加整页，再逐个绘制文本/图片，最后输出新的 PDF 字节。
    /// </summary>
    /// <param name="pdf">原 PDF 字节（不会被修改）。</param>
    /// <param name="request">盖章请求。</param>
    /// <returns>盖章后的 PDF 字节。内容直接画在页面内容层，输出天然是压平的。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PdfStamp.PageNumber"/> 越界。</exception>
    /// <exception cref="Exceptions.PdfDocumentException">原字节不是合法 PDF、图片无法解码，或画文字时没有可用字体。</exception>
    byte[] Stamp(byte[] pdf, PdfStampRequest request);

    /// <summary>
    /// 从零新建一份 PDF：按 <see cref="PdfStampRequest.AppendPages"/> 造页，再按
    /// <see cref="PdfStampRequest.Stamps"/> 绘制。
    /// </summary>
    /// <remarks>
    /// ★ 存在的理由是那些**不依附于任何原件**的页面：完成证书、回执、封面。它们必须是
    /// 独立文件而不是追加到原件末尾——一旦追加，原件就不再是当初签的那一份，而且
    /// 「证书里写上成品的哈希」这件事会变成先有鸡还是先有蛋（追加会改哈希）。
    ///
    /// 没有指定尺寸时按 US Letter（612 × 792 pt）。
    /// </remarks>
    /// <param name="request">造页 + 绘制请求；<see cref="PdfStampRequest.AppendPages"/> 不得为空（否则出来的是零页文档）。</param>
    /// <exception cref="ArgumentException"><see cref="PdfStampRequest.AppendPages"/> 为空。</exception>
    /// <exception cref="Exceptions.PdfDocumentException">图片无法解码，或画文字时没有可用字体。</exception>
    byte[] Create(PdfStampRequest request);
}
