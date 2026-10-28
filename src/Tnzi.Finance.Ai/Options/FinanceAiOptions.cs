namespace Tnzi.Finance.Ai.Options;

/// <summary>
/// Configuration for the AI-backed receipt extractor.
/// </summary>
/// <remarks>
/// When <see cref="Provider"/>/<see cref="Model"/> are unset, the AI module's default provider/model
/// are used. Bound from the <c>Finance:Ai</c> configuration section.
/// </remarks>
[ConfigSection("Finance:Ai")]
public class FinanceAiOptions
{
    /// <summary>AI provider used for extraction (null falls back to the AI module default).</summary>
    public string? Provider { get; set; }

    /// <summary>Model used for extraction (null falls back to the provider default).</summary>
    public string? Model { get; set; }

    /// <summary>Maximum size (MB) of a single receipt file.</summary>
    public int MaxFileSizeMb { get; set; } = 20;

    /// <summary>
    /// Maximum number of characters of extracted PDF text sent to the model.
    /// </summary>
    /// <remarks>
    /// ★ 这是闸门不是调优项：字节大小有两道闸门，而<b>文本长度一道都没有</b>。
    /// 一份 20 MB 的纯文字 PDF 解出来是上百万字符，整份原样拼进提示词 ——
    /// 换回的要么是一张与收据金额毫无关系的账单，要么是供应商侧的长度报错，
    /// 而两者都由一次普通上传触发。
    /// 收据正文通常在 5000 字符以内；默认留足余量给多页发票，超出部分截断并注明，
    /// 不是拒绝：一份正文正常、后面附了几页条款的发票仍应当能录进来。
    /// </remarks>
    public int MaxPdfTextChars { get; set; } = 20000;

    /// <summary>The vision content types used when <see cref="VisionContentTypes"/> is left empty.</summary>
    public static readonly IReadOnlyList<string> DefaultVisionContentTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    /// <summary>The single entry that disables the vision gate: <c>["*"]</c> sends whatever was uploaded.</summary>
    public const string AnyVisionContentType = "*";

    /// <summary>
    /// Image content types the vision model accepts. Empty means <see cref="DefaultVisionContentTypes"/>;
    /// <c>["*"]</c> means no gate.
    /// </summary>
    /// <remarks>
    /// ★ 存在的理由是**错误消息的可操作性**，不是安全。iPhone 拍的 <c>.heic</c> 与扫描仪出的
    /// <c>.tiff</c> 现在能被识别成 <c>image/*</c>（见 <c>FileTypeHelper</c>），但主流视觉模型
    /// 并不收这两种；直接送过去只会换回一句供应商侧的报错，最终对用户显示成
    /// 「提取失败，详见服务端日志」—— 他无从知道该怎么做。这张表让门禁前移到能说清楚
    /// 「请上传 JPEG/PNG 或 PDF」的位置。
    /// <para>
    /// 因此它是**配置而非常量**：接了自己 provider（或过一道转码）的部署写出完整清单即可，
    /// 写 <c>["*"]</c> 则完全不拦。数组型配置不进设置中心，只从 appsettings 绑定。
    /// </para>
    /// <para>
    /// ★ <b>默认值必须是空数组，缺省清单在读取处补</b>（见 <see cref="ResolveVisionContentTypes"/>）：
    /// .NET 的配置绑定对数组是<b>追加</b>语义，预置的四项会与配置合并：配了 <c>["image/png"]</c>
    /// 想收窄的部署得到五项；而 JSON 空数组不产生任何配置键，与「没配」无从区分，
    /// 所以「不拦」不能用空数组表达，只能写成 <c>["*"]</c>。
    /// </para>
    /// </remarks>
    public string[] VisionContentTypes { get; set; } = [];

    /// <summary>
    /// The effective vision content types: the configured list, <see cref="DefaultVisionContentTypes"/>
    /// when none is configured, or <see langword="null"/> when <c>["*"]</c> disables the gate.
    /// </summary>
    public IReadOnlyList<string>? ResolveVisionContentTypes()
    {
        if (VisionContentTypes.Length == 0)
            return DefaultVisionContentTypes;
        return VisionContentTypes.Any(t => string.Equals(t?.Trim(), AnyVisionContentType, StringComparison.Ordinal))
            ? null
            : VisionContentTypes;
    }
}
