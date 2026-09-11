using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Tnzi.Template.Services;

/// <summary>
/// 出厂模板的「可刷新但不覆盖用户编辑」播种语义
/// </summary>
/// <remarks>
/// 模块随程序集分发的出厂模板正文，播种时是 additive、永不覆盖的 —— 保护的是管理端对版式的编辑。
/// 代价是：<b>框架代码里修好的模板正文，到不了一个已经播种过的库</b>。没有报错，只是纸印错，
/// 而每个消费应用都得自己想到要写一条数据迁移去删行重播。
/// <para>
/// 本类把「这一行还是不是出厂版」变成一个可判定的问题：播种时把<b>写下去的那份正文的指纹</b>
/// 一并存进 <see cref="Entities.Template.Metadata"/>。此后：
/// </para>
/// <list type="bullet">
/// <item><b>当前正文的指纹 == 存下的指纹</b> → 自我们播下去之后没人动过 → 可以安全刷新；</item>
/// <item><b>不相等</b> → 有人改过 → 永不覆盖（这正是 additive 当初存在的全部理由）。</item>
/// </list>
/// <para>
/// ★ <b>指纹只覆盖正文，不含描述</b>。两条理由：把描述算进去，用户改一句描述就会让
/// 几何修复永远刷不进去（代价方向错了）；而刷新时也<b>不动描述</b>，所以用户对描述的
/// 编辑不会丢。两件事各自独立，谁也不挡谁。
/// </para>
/// </remarks>
public static class SeededTemplateRefresh
{
    /// <summary>
    /// 指纹在 <see cref="Entities.Template.Metadata"/> JSON 里的键。
    /// </summary>
    /// <remarks>
    /// <c>Metadata</c> 的键名由模板作者自定义、框架不做约定，所以这里带 <c>tnzi:</c> 前缀
    /// 圈出一小块保留命名空间，与作者自己的键不会撞。
    /// </remarks>
    public const string MetadataKey = "tnzi:seedFingerprint";

    /// <summary>
    /// 正文指纹（SHA-256 十六进制）。
    /// </summary>
    /// <remarks>
    /// ★ 先把换行统一成 <c>\n</c> 再算。存储与传输环节改写换行是常事（CRLF ⇄ LF），
    /// 而那不是「用户改过版式」—— 不归一化的话，一次换行重写就会让整行被永久判成用户所有，
    /// 从此再也刷不动，且没有任何症状。
    /// </remarks>
    public static string Fingerprint(string? content)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>取出这一行上次被播种时记下的正文指纹；没有则返回 <see langword="null"/>。</summary>
    public static string? ReadFingerprint(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
            return null;

        try
        {
            return JsonNode.Parse(metadata) is JsonObject obj && obj.TryGetPropertyValue(MetadataKey, out var node)
                ? node?.GetValue<string>()
                : null;
        }
        catch (JsonException)
        {
            // Metadata 不是合法 JSON —— 那是别人的数据，读不懂就当作没有指纹。
            return null;
        }
    }

    /// <summary>
    /// 把指纹写进 metadata JSON，保留其余键。
    /// </summary>
    /// <returns>
    /// 新的 metadata 串；当既有 metadata <b>不是 JSON 对象</b>（数组、标量、或根本不是 JSON）时
    /// 返回 <see langword="null"/> —— 那是别人的数据结构，看不懂就一个字节都不动。
    /// 调用方应把这种行当作用户所有、跳过刷新。
    /// </returns>
    public static string? TryWriteFingerprint(string? metadata, string fingerprint)
    {
        Check.NotNullOrWhiteSpace(fingerprint);

        JsonObject obj;
        if (string.IsNullOrWhiteSpace(metadata))
        {
            obj = new JsonObject();
        }
        else
        {
            try
            {
                if (JsonNode.Parse(metadata) is not JsonObject parsed)
                    return null;
                obj = parsed;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        obj[MetadataKey] = fingerprint;
        return obj.ToJsonString();
    }

    /// <summary>
    /// 这一行是否仍归框架所有（即：可以用新的出厂正文刷新它）。
    /// </summary>
    /// <param name="metadata">行上的 <see cref="Entities.Template.Metadata"/>。</param>
    /// <param name="currentContent">行上当前的正文。</param>
    /// <param name="lastModificationTime">行上的最后修改时间（<see langword="null"/> = 插入之后从未被更新过）。</param>
    /// <remarks>
    /// ★★ <b>存量行没有指纹</b>（它们在这个机制存在之前就播下去了），这是本机制最需要想清楚的一处。
    /// 两个方向都不能选：把它们一律当出厂版刷掉，会静默销毁机制诞生前的用户编辑；
    /// 一律当用户所有，则所有存量行永远刷不动 —— 而那正是本机制要解决的问题本身。
    /// <para>
    /// 判据取 <c>LastModificationTime == null</c>：审计拦截器只在 <c>Modified</c> 时写这个字段，
    /// 所以它为 null 严格等价于「插入之后没有任何人更新过这一行」= 仍是出厂版。
    /// 这与消费应用手写迁移时用的判据<b>逐字相同</b>（他们的 SQL 就是删
    /// <c>LastModificationTime IS NULL</c> 的行），所以既不会与已经跑过的迁移打架，
    /// 也不会把已经刷新过的行再判成陈旧。
    /// </para>
    /// <para>
    /// ★ 这条只用一次：一旦被采纳，指纹就写下去了，往后一律走指纹比对
    /// （采纳那次写入本身会把 <c>LastModificationTime</c> 置上，但那时指纹已在，不再走这条分支）。
    /// 反过来，一个在机制诞生前被人编辑过的行会永远走「跳过」—— 想让它回到出厂版，
    /// 删掉该行即可，下次启动自动播回，与今天的做法一致。
    /// </para>
    /// </remarks>
    public static bool IsFactoryOwned(string? metadata, string? currentContent, DateTime? lastModificationTime)
    {
        var stored = ReadFingerprint(metadata);
        return stored == null
            ? lastModificationTime == null
            : string.Equals(stored, Fingerprint(currentContent), StringComparison.OrdinalIgnoreCase);
    }
}
