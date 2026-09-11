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

    /// <summary>
    /// Image content types the vision model accepts. Empty means no gate: send whatever was uploaded.
    /// </summary>
    /// <remarks>
    /// ★ 存在的理由是**错误消息的可操作性**，不是安全。iPhone 拍的 <c>.heic</c> 与扫描仪出的
    /// <c>.tiff</c> 现在能被识别成 <c>image/*</c>（见 <c>FileTypeHelper</c>），但主流视觉模型
    /// 并不收这两种；直接送过去只会换回一句供应商侧的报错，最终对用户显示成
    /// 「提取失败，详见服务端日志」—— 他无从知道该怎么做。这张表让门禁前移到能说清楚
    /// 「请上传 JPEG/PNG 或 PDF」的位置。
    /// <para>
    /// 因此它是**配置而非常量**：接了自己 provider（或过一道转码）的部署把格式加进来即可，
    /// 留空则完全不拦。数组型配置不进设置中心，只从 appsettings 绑定。
    /// </para>
    /// </remarks>
    public string[] VisionContentTypes { get; set; } = ["image/jpeg", "image/png", "image/gif", "image/webp"];
}
