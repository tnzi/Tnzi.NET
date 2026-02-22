namespace Tnzi.Storage.Mappings;

/// <summary>
/// FileRecord 到 FileInfoDto 的映射配置
/// </summary>
public class FileRecordMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        context.NewConfig<FileRecord, FileInfoDto>()
            .Map(dest => dest.FileId, src => src.Id)
            .Map(dest => dest.FilePath, src => src.Path ?? string.Empty)
            .Map(dest => dest.FileSize, src => src.Size);
    }
}

