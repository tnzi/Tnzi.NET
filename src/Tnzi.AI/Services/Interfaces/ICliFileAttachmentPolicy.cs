namespace Tnzi.AI.Services;

/// <summary>
/// 决定一轮交给外部 CLI 运行时的对话收不收<b>文件附件</b>：不是图片（PNG / JPEG / GIF / WebP）也不是 PDF 的附件，
/// 例如 <c>.eml</c>、<c>.txt</c>、<c>.csv</c>、<c>.docx</c>、<c>.zip</c>、源文件。
/// </summary>
/// <remarks>
/// <para>
/// 图片与 PDF 随提示作为内容块交给模型，与此无关，照旧。文件不交给模型：它随消息存档（<c>AgentAttachment</c>，<c>Origin = Upload</c>，
/// 真实文件名与媒体类型），由消费方在运行启动时放到 agent 能读到的地方（典型做法：启动档案的 contributor
/// 按运行 id 取出这一轮的上传附件，写进工作目录），agent 用自己的工具打开它。CLI 收到的这一轮里写着每个文件的名字、大小与位置
/// （位置由启动档案的 <c>AttachmentPaths</c> 给出），所以模型知道要去打开它，而不会以为自己已经看过。
/// </para>
/// <para>
/// 只有放得下文件的一轮才该收：例如运行在用户工作区里的一轮收，运行在没有文件系统可写的地方的一轮不收。
/// 不收的一轮带着文件时整条消息以 415（<c>AI_UNSUPPORTED_MEDIA_TYPE</c>）拒绝，什么都不写，与没有注册这个接口时相同。
/// </para>
/// <para>
/// <b>没有注册时一律不收</b>（此前的行为）。注册多个时全部放行才收。在消费方模块的 <c>ConfigureServicesAsync</c> 里注册为 Scoped：
/// </para>
/// <code>
/// context.Services.AddScoped&lt;ICliFileAttachmentPolicy, WorkspaceFileAttachments&gt;();
/// </code>
/// <para>
/// 只在这一轮确实带着文件时调用。聊天门面在写任何东西之前问一次，调度器入队时再问一次（直接调调度器的调用方没有门面），
/// 两次给出的身份相同（第二次多了 <see cref="CliFileAttachmentRequest.UserMessageId"/>）。大小按
/// <c>AI:Chat:Attachments:MaxFileBytes</c>，条数与图片、PDF 合计按 <c>AI:Chat:Attachments:MaxCount</c>。
/// </para>
/// </remarks>
public interface ICliFileAttachmentPolicy
{
    /// <summary>这一轮收不收它带着的文件。</summary>
    Task<bool> AcceptsFilesAsync(CliFileAttachmentRequest request, CancellationToken cancellationToken = default);
}
