using System.Security.Cryptography;

namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行流水行文本字段的列宽，以及导入时越界该怎么办。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么在摄取器而不在解析器</b>：OFX / CSV 解析器与 <see cref="IBankFeedProvider"/>
/// 是三条来源，后者还是消费应用可替换的扩展点；归一化写进某一条来源，换一条就绕过去了。
/// <c>BankStatementIngestor.PersistDeduplicatedTransactionsAsync</c> 是三条来源<b>跨进持久化</b>的
/// 唯一入口，是不依赖「每个来源都记得做」的位置（判据同 <see cref="ReceiptFieldLimits"/>）。
/// </para>
/// <para>
/// ★ <b>机器给的值归一化，不拒绝</b>：文件来自银行，操作员改不了它。此前原样赋值，
/// SQL Server / PostgreSQL 上一行超长就在循环中间抛 500 —— 批次头与前 N-1 行已各自提交、
/// 计数停在 0/0、事件没发；重传时前 N-1 行按去重跳过、那一行再炸一次，这份对账单永远导不完。
/// </para>
/// <para>
/// ★ <b>给人看的截断、当键用的不截断</b>，判据是「错的值比空着更糟还是更好」：
/// 摘要与收款人是人读的，留开头有用；<b>参考号</b>被银行规则拿去精确匹配，截出来的看似合法却是
/// 另一个参考号 → 丢弃；<b>ExternalId</b> 是去重键且必填，两个只在第 300 位不同的 FITID 截成
/// 同一个值会让第二笔真实流水被计成重复而静默丢掉 → 折叠成哈希（确定性，重传仍能去重）。
/// </para>
/// <para>
/// 常量由 <c>BankTransactionConfiguration</c> 直接引用，两处不可能漂移。
/// ⚠️ 测试库是 SQLite，它不执行 varchar 长度约束；断言落在「存下来的值已是归一化后的形状」。
/// </para>
/// </remarks>
internal static class BankTransactionFieldLimits
{
    /// <summary><see cref="BankTransaction.Description"/> 列宽</summary>
    internal const int DescriptionMaxLength = 512;

    /// <summary><see cref="BankTransaction.Payee"/> 列宽</summary>
    internal const int PayeeMaxLength = 256;

    /// <summary><see cref="BankTransaction.Reference"/> 列宽</summary>
    internal const int ReferenceMaxLength = 128;

    /// <summary><see cref="BankTransaction.ExternalId"/> 列宽</summary>
    internal const int ExternalIdMaxLength = 256;

    /// <summary>
    /// <see cref="BankImportBatch.FileName"/> 列宽。批次头是同一次导入的一部分，故与行字段同一处登记；
    /// 但它是<b>人给的</b>（上传时的文件名），越界按 <see cref="ReceiptFieldLimits"/> 的判据拒绝而不截断。
    /// </summary>
    internal const int ImportFileNameMaxLength = 256;

    private const string FoldedExternalIdPrefix = "ext:";

    /// <summary>摘要：超长截断保留开头（给人看的）。</summary>
    internal static string? ClampDescription(string? value) => Truncate(value, DescriptionMaxLength);

    /// <summary>收款人：超长截断保留开头（给人看的）。</summary>
    internal static string? ClampPayee(string? value) => Truncate(value, PayeeMaxLength);

    /// <summary>参考号：超长丢弃 —— 截出来的是另一个参考号，银行规则会按它误匹配。</summary>
    internal static string? NormalizeReference(string? value)
        => value != null && value.Length > ReferenceMaxLength ? null : value;

    /// <summary>
    /// 去重键：超长折叠成 <c>ext:</c> + SHA-256 十六进制（68 字符，确定性），绝不截断。
    /// </summary>
    internal static string FoldExternalId(string externalId)
    {
        Check.NotNull(externalId);
        if (externalId.Length <= ExternalIdMaxLength)
            return externalId;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(externalId));
        return FoldedExternalIdPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? Truncate(string? value, int max)
        => value != null && value.Length > max ? value[..max] : value;
}
