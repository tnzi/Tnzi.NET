namespace Tnzi.Storage.Sanitization;

/// <summary>
/// 上传净化器：在文件交给存储提供者<strong>之前</strong>检查或改写它。
/// </summary>
/// <remarks>
/// <para>
/// <strong>可选能力。</strong>框架不注册任何实现，管线为空时 <c>SaveAsync</c> 一个净化器都不调，
/// 不做任何额外读流、不产生任何开销。不需要这项能力的应用完全不受影响。
/// </para>
/// <para>
/// <strong>两类净化互不能替代，通常要分别注册：</strong>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>保护上传者</strong>：剥离元数据（照片里的 GPS 坐标、设备序列号）。
/// 这类处理越早越好，理想位置其实在浏览器端，服务端这层是兜底。
/// </description></item>
/// <item><description>
/// <strong>保护下游</strong>：病毒扫描、按 magic number 核对真实类型、重编码。
/// 这类<strong>必须</strong>在服务端做，客户端做的任何检查上传者都能绕过。
/// </description></item>
/// </list>
/// <para>
/// <strong>实现方注意流的所有权。</strong>本模块对流的既有约定是：调用方拥有传入的流，
/// 且不得假设上传后它还可读（见 <c>IFileStorage.UploadAsync</c>）。净化器同理：
/// <list type="bullet">
/// <item><description>不要 dispose <see cref="UploadSanitizationContext.Content"/>，它不属于你；</description></item>
/// <item><description>返回替换流时交出所有权，<strong>由管线负责 dispose</strong>；</description></item>
/// <item><description>读完流后不必回到流首，管线会在交给下一个净化器前处理。</description></item>
/// </list>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "净化管线的上下文字段可能随重编码、隔离沙箱等场景补充")]
public interface IUploadSanitizer
{
    /// <summary>
    /// 管线内的执行顺序，小的先执行。
    /// </summary>
    /// <remarks>
    /// 约定：类型/大小一类的<strong>廉价拒绝</strong>放前面（例如 0 到 99），
    /// 扫描与重编码一类的<strong>昂贵处理</strong>放后面（例如 100 以上）。
    /// 顺序错了不会出错，只是白白对一个注定被拒的文件做了重活。
    /// </remarks>
    int Order => 100;

    /// <summary>
    /// 检查或改写一个待上传的文件。
    /// </summary>
    /// <param name="context">文件名、扩展名、内容类型与当前内容流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>放行、替换内容，或拒绝。</returns>
    Task<UploadSanitizationResult> SanitizeAsync(
        UploadSanitizationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 净化上下文。
/// </summary>
/// <param name="FileName">上传时的原始文件名。</param>
/// <param name="Extension">从文件名解析出的扩展名（含点，可能为空）。</param>
/// <param name="ContentType">按扩展名推断的内容类型，<strong>不可信</strong>：它来自文件名而不是内容。</param>
/// <param name="Content">当前内容流。上一个净化器替换过内容时，这里是替换后的流。</param>
public sealed record UploadSanitizationContext(
    string FileName,
    string Extension,
    string ContentType,
    Stream Content);

/// <summary>
/// 净化结果。
/// </summary>
public sealed class UploadSanitizationResult
{
    private UploadSanitizationResult(bool rejected, string? reason, Stream? replacement)
    {
        Rejected = rejected;
        Reason = reason;
        Replacement = replacement;
    }

    /// <summary>是否拒绝本次上传。</summary>
    public bool Rejected { get; }

    /// <summary>拒绝原因（英文，会作为失败消息返回给调用方）。</summary>
    public string? Reason { get; }

    /// <summary>替换后的内容流；<c>null</c> 表示内容未改变。</summary>
    public Stream? Replacement { get; }

    /// <summary>放行，内容不变。</summary>
    public static UploadSanitizationResult Unchanged() => new(false, null, null);

    /// <summary>
    /// 放行，但用新流替换内容（剥离元数据、重编码等）。
    /// </summary>
    /// <param name="replacement">替换流，所有权交给管线。</param>
    public static UploadSanitizationResult Replaced(Stream replacement)
        => new(false, null, Check.NotNull(replacement));

    /// <summary>
    /// 拒绝上传。
    /// </summary>
    /// <param name="reason">
    /// 原因（英文）。会返回给上传者，<strong>不要写入内部路径、扫描引擎版本一类的细节</strong>。
    /// </param>
    public static UploadSanitizationResult Reject(string reason)
        => new(true, Check.NotNullOrWhiteSpace(reason), null);
}
