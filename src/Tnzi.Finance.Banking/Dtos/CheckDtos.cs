namespace Tnzi.Finance.Banking.Dtos;

/// <summary>
/// 支票记录 DTO
/// </summary>
public class BankCheckDto
{
    public Guid Id { get; set; }
    public Guid BankAccountId { get; set; }

    /// <summary>银行账户档案名称（服务层补齐）</summary>
    public string? BankAccountName { get; set; }

    public long CheckNumber { get; set; }
    public CheckStatus Status { get; set; }
    public Guid? PaymentEntryId { get; set; }

    /// <summary>关联付款单编号（服务层补齐）</summary>
    public string? PaymentNumber { get; set; }

    public string? PayeeName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? PrintedTime { get; set; }
    public bool IsManual { get; set; }
    public string? VoidReason { get; set; }
    public Guid? ReplacedByCheckId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 打印队列项（待打印的 Posted Outbound 支票付款单）
/// </summary>
public class CheckQueueItemDto
{
    /// <summary>付款单ID</summary>
    public Guid PaymentEntryId { get; set; }

    /// <summary>付款单编号</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>出款银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>出款银行账户名称</summary>
    public string? BankAccountName { get; set; }

    /// <summary>收款人（供应商名称）</summary>
    public string? PayeeName { get; set; }

    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Memo { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// 打印支票请求（逐张按分配号打印并合并为一份 PDF）
/// </summary>
public class PrintChecksDto
{
    /// <summary>待打印的付款单ID（须均在打印队列内且共享同一银行账户）</summary>
    public List<Guid> PaymentEntryIds { get; set; } = null!;

    /// <summary>签发日期（null 用付款单日期）</summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>
    /// 本次使用的版式模板名（<b>单次覆盖</b>；留空 = 跟随银行账户档案，档案也未设则用出厂默认模板）
    /// </summary>
    /// <remarks>
    /// 让"这一批用凭证式、那一批用每页三张"不必去改银行档案。取值来自
    /// <c>GET admin/finance/checks/templates</c> 的版式目录。
    /// </remarks>
    public string? TemplateName { get; set; }
}

/// <summary>
/// 登记手工支票（手写票入登记簿，显式号撞号 409）
/// </summary>
public class RegisterManualCheckDto
{
    public Guid BankAccountId { get; set; }
    public long CheckNumber { get; set; }
    public string? PayeeName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime IssueDate { get; set; }

    /// <summary>可选关联付款单</summary>
    public Guid? PaymentEntryId { get; set; }
}

/// <summary>
/// 作废支票
/// </summary>
public class VoidCheckDto
{
    public string? Reason { get; set; }
}

/// <summary>
/// 毁票登记（损坏/对齐失败的空白票占号留痕，可推进 NextCheckNumber）
/// </summary>
public class SpoilCheckDto
{
    public Guid BankAccountId { get; set; }
    public long CheckNumber { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 预览支票请求（零副作用：不分配支票号、不写登记簿、不动账）
/// </summary>
/// <remarks>
/// 支票号显示为"下一个待分配号"的预览值（peek 不 consume），提交打印时才真正分配。
/// 入参约束与 <see cref="PrintChecksDto"/> 一致（须均在打印队列内且共享同一银行账户），
/// 保证"所见即将打"。
/// </remarks>
public class PreviewChecksDto
{
    /// <summary>待预览的付款单ID（须均在打印队列内且共享同一银行账户）</summary>
    public List<Guid> PaymentEntryIds { get; set; } = null!;

    /// <summary>签发日期（null 用付款单日期）</summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>本次使用的版式模板名（单次覆盖；留空 = 跟随银行账户档案）</summary>
    /// <remarks>与 <see cref="PrintChecksDto.TemplateName"/> 同口径，保证"所见即将打"。</remarks>
    public string? TemplateName { get; set; }
}

/// <summary>
/// 临时（无付款单）支票预览请求：直接从"将要支付"的明细渲染，<b>零副作用</b>
/// （不过账、不建付款单、不分配支票号、不写登记簿）。
/// </summary>
/// <remarks>
/// 用于"先预览、点打印才落库"的支付流：预览时账单尚未结算，故没有付款单可引用。
/// 支票号取银行档案当前 <c>NextCheckNumber</c> 起连号推演（peek 不 consume），并打不可流通水印。
/// </remarks>
public class AdHocCheckPreviewDto
{
    /// <summary>出账（资金）总账科目ID；据此解析银行账户档案（版式/偏移/抬头/支票号序列）。</summary>
    public Guid FundsAccountId { get; set; }

    /// <summary>签发日期（null 用今天）。</summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>本次使用的版式模板名（单次覆盖；留空 = 跟随银行账户档案）。</summary>
    public string? TemplateName { get; set; }

    /// <summary>逐张支票明细（一个收款人一张）。</summary>
    public List<AdHocCheckItemDto> Items { get; set; } = null!;
}

/// <summary>临时支票预览的单张明细（一个收款人一张，金额=该收款人本次合计）。</summary>
/// <remarks>
/// 收款人按框架 <c>Vendor</c> 解析（名/址与 <see cref="PrintChecksDto"/> 落库时 <b>同源</b>），
/// 保证"预览==开票"。上层应按 (收款人, 币种) 分组汇总后每组给一条，与结算分组一致。
/// </remarks>
public class AdHocCheckItemDto
{
    /// <summary>收款人（框架 <c>Vendor</c>）ID。</summary>
    public Guid PayeeVendorId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>币种（仅用于金额大写与票面币种字样）；null/空则取本位币 <c>Finance:BaseCurrency</c>。</summary>
    public string? Currency { get; set; }

    public string? Memo { get; set; }

    /// <summary>
    /// 存根附加行（消费应用自己域里的「标签 : 值」，如案卷号 / 客户 / 事由）。
    /// </summary>
    /// <remarks>
    /// ★ 这条路径上<b>还没有付款单</b>，无键可查，故不经 <c>ICheckStubLineProvider</c>
    /// 而由请求直接携带 —— 与本 DTO 上收款人、金额、摘要的来源一致。
    /// 上层应当让这里的行与随后 <c>PrintChecksDto</c> 落库时提供者给出的行同源，
    /// 否则"预览 == 开票"在存根上不成立。
    /// 条数与长度超限时按 <c>CheckStubLineLimits</c> 归一化（不拒绝）。
    /// </remarks>
    public List<CheckStubLine>? StubLines { get; set; }
}

/// <summary>
/// 生成的支票文件（由服务在事务内渲染，控制器落地为 File 下载）
/// </summary>
/// <remarks>
/// 内容格式取决于生效的 <c>ICheckDocumentRenderer</c>：模板驱动渲染器出 HTML
/// （所见即所得预览 + 浏览器 <c>@media print</c> 打印），PdfSharp 渲染器出 PDF。
/// <see cref="ContentType"/> 与 <see cref="FileName"/> 的后缀由渲染器自报，调用方据此设置响应 MIME。
/// </remarks>
public class CheckFileDto
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型（如 <c>text/html</c> / <c>application/pdf</c>）</summary>
    public string ContentType { get; set; } = "application/pdf";

    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 支票记录查询
/// </summary>
public class CheckQueryDto : PagedQueryDto
{
    public Guid? BankAccountId { get; set; }
    public CheckStatus? Status { get; set; }

    /// <summary>按关联付款单过滤（回答"这笔付款是哪张支票付的"；含其重打链上的历史票）</summary>
    public Guid? PaymentEntryId { get; set; }

    /// <summary>关键字（收款人/作废原因模糊匹配）</summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 支票版式目录项（出厂内置版式，或模板库里用户自建的一条模板）
/// </summary>
/// <remarks>
/// 由 <see cref="Services.ICheckTemplateCatalog"/> 汇总，管理端据此画版式选择器 ——
/// 名称是选中后要写进 <c>BankAccount.CheckTemplateName</c> 的值，其余字段供人判断
/// "这套版式是给谁用的、印在什么纸上、一页几张"。
/// </remarks>
public class CheckTemplateDto
{
    /// <summary>模板名（模板库内 Module=<c>Tnzi.Finance</c> / Category=<c>Check</c> 下唯一）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>展示名（英文；面向用户，呈现端可自行本地化）</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>说明</summary>
    public string? Description { get; set; }

    /// <summary>适用区域（如 <c>CA</c> / <c>US</c>；null = 不限区域）</summary>
    /// <remarks>刻意用字符串而非枚举：框架不预设自己认识哪些国家的票据规范。</remarks>
    public string? Region { get; set; }

    /// <summary>纸张（如 <c>Letter</c>）</summary>
    public string? PaperSize { get; set; }

    /// <summary>每页支票张数</summary>
    public int ChecksPerPage { get; set; } = 1;

    /// <summary>支票在页面上的位置（凭证式版式才有；每页多张为 null）</summary>
    public CheckPosition? Position { get; set; }

    /// <summary>支持的票纸类型（预印 / 白纸；白纸版式会现打 MICR 磁码行）</summary>
    public List<CheckStockType> SupportedStockTypes { get; set; } = new();

    /// <summary>是否为框架出厂版式（false = 消费应用在模板管理端自建的模板）</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>模板是否启用（库内行的 <c>IsActive</c>；尚未播种的出厂版式为 true）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>模板库里是否已有对应的行（false = 出厂版式尚未播种，下次启动会补上）</summary>
    public bool IsSeeded { get; set; }
}
