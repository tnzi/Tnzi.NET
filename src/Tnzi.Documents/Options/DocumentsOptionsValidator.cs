namespace Tnzi.Documents.Options;

/// <summary>
/// <see cref="DocumentsOptions"/> 的验证器。
/// </summary>
/// <remarks>
/// 显式配置的 LibreOffice 路径在**启动期**就校验存在性：配错了要立刻在启动日志里说清楚，
/// 而不是等到第一次有人上传 .docx 才报错。未配置（走自动探测）不在这里判定 ——
/// 那是「可选能力未安装」，由 <c>DocumentsModule</c> 启动时输出警告、调用时抛异常。
/// </remarks>
public class DocumentsOptionsValidator : OptionsValidatorBase<DocumentsOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(DocumentsOptions options, List<string> errors)
    {
        if (options.ConversionTimeoutSeconds is < 5 or > 3600)
            AddError(errors, nameof(DocumentsOptions.ConversionTimeoutSeconds), "must be between 5 and 3600 seconds.", "5..3600");

        if (!string.IsNullOrWhiteSpace(options.LibreOfficePath)
            && !File.Exists(options.LibreOfficePath)
            && !Directory.Exists(options.LibreOfficePath))
        {
            AddError(errors, nameof(DocumentsOptions.LibreOfficePath),
                $"points to '{options.LibreOfficePath}', which is neither an existing file nor an existing directory. Leave it empty to auto-detect LibreOffice.");
        }

        if (!string.IsNullOrWhiteSpace(options.ProfileDirectory) && !Path.IsPathRooted(options.ProfileDirectory))
        {
            AddError(errors, nameof(DocumentsOptions.ProfileDirectory),
                "must be an absolute path (LibreOffice takes it as a file:// URL).");
        }
    }
}
