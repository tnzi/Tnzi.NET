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
