namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 支票版式目录（契约在银行域，本模块零渲染库引用）
/// </summary>
/// <remarks>
/// 回答两个问题，两者共用同一份出厂版式声明，因此不可能漂移：
/// <list type="number">
/// <item><b>有哪些版式可选</b>（<see cref="GetAllAsync"/>）—— 代码内置的出厂版式描述 +
///       模板库里用户自建的模板行，合并成一份可枚举清单，管理端据此画版式选择器；</item>
/// <item><b>这一次到底渲染哪一份</b>（<see cref="Resolve"/>）—— 把「请求指定的模板名 +
///       账户版式」解析成实际模板与它的每页张数。</item>
/// </list>
/// 默认实现由可选子模块 <c>Tnzi.Finance.Documents</c> 提供（内置版式随该程序集分发）。
/// 未加载时 <c>ICheckService.GetTemplatesAsync</c> 返回 501 引导，与
/// <see cref="ICheckDocumentRenderer"/> 缺席时的打印端点同构。
/// </remarks>
public interface ICheckTemplateCatalog
{
    /// <summary>出厂版式 + 库内自建模板的合并清单（含足够画选择器的元数据）。</summary>
    Task<Result<List<CheckTemplateDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析本次渲染实际生效的模板与每页张数。
    /// </summary>
    /// <param name="requestedTemplateName">
    /// 请求 / 银行档案上指定的模板名；留空则按 <paramref name="layout"/> 取出厂默认模板。
    /// </param>
    /// <param name="layout">账户版式（仅在未指定模板名时用于选默认模板）。</param>
    CheckTemplateResolution Resolve(string? requestedTemplateName, CheckLayout layout);
}

/// <summary>
/// 模板解析结果（实际模板名 + 该模板每页印几张支票）
/// </summary>
/// <remarks>
/// 每页张数由<b>模板</b>声明而不是由 <see cref="CheckLayout"/> 决定：几何形状写在模板里，
/// 枚举只是没选模板时的默认选择器。渲染器据此把一批支票切页，模板只管排版。
/// </remarks>
public sealed record CheckTemplateResolution(string TemplateName, int ChecksPerPage);
