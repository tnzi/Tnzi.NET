namespace Tnzi.Finance.Services;

/// <summary>
/// 对账单文档渲染（**契约留核心，核心零渲染库引用**）
/// </summary>
/// <remarks>
/// 默认实现由可选子模块 <c>Tnzi.Finance.Documents</c> 提供（模板驱动，与支票同一
/// 套机制）；未加载时对账单的**数据**照常可取，只有出文档的端点返回 501 引导——
/// 与 <c>ICheckDocumentRenderer</c> / <c>IReceiptExtractor</c> 同一范式。
///
/// 与支票渲染器不同，本契约留在**核心**：对账单是 A/R 概念，不属于银行域。
/// </remarks>
public interface IStatementRenderer
{
    /// <summary>产物的内容类型（默认 HTML：模板渲染器出的是可打印页面）</summary>
    string ContentType => "text/html";

    /// <summary>产物的文件后缀</summary>
    string FileExtension => ".html";

    /// <summary>渲染一张对账单</summary>
    Task<Result<byte[]>> RenderAsync(CustomerStatementDto statement, CancellationToken cancellationToken = default);
}
