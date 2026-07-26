namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 默认 EFT 文件组装器（按格式分发到 NACHA / CPA-005 纯函数构建器）
/// </summary>
public class DefaultEftFileComposer : IEftFileComposer
{
    public Result<EftComposeResult> Compose(EftComposeRequest request)
    {
        Check.NotNull(request);

        try
        {
            var content = request.Format switch
            {
                EftFileFormat.Nacha => NachaFileBuilder.Build(request),
                EftFileFormat.Cpa005 => Cpa005FileBuilder.Build(request),
                _ => throw new BusinessException("Unsupported EFT file format.")
            };
            var extension = request.Format == EftFileFormat.Nacha ? "ach" : "txt";
            return Result<EftComposeResult>.Success(new EftComposeResult { Content = content, FileExtension = extension });
        }
        catch (BusinessException ex)
        {
            return Result<EftComposeResult>.Failure(ex.Message, ex.HttpStatusCode);
        }
    }
}
