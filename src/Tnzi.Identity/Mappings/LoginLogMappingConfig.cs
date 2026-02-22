
namespace Tnzi.Identity.Mappings;

/// <summary>
/// LoginLog 映射配置
/// </summary>
public class LoginLogMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // LoginLog → LoginLogDto
        // 将 Status 枚举映射为 IsSuccess 布尔值
        // 将 CreationTime 映射为 LoginTime
        context.NewConfig<LoginLog, LoginLogDto>()
            .Map(dest => dest.IsSuccess, src => src.Status == LoginStatus.Success)
            .Map(dest => dest.LoginTime, src => src.CreationTime);

        // CreateLoginLogDto → LoginLog (如果有创建 DTO)
        // context.NewConfig<CreateLoginLogDto, LoginLog>()
        //     .Map(dest => dest.CreationTime, src => DateTime.UtcNow)
        //     .Ignore(dest => dest.Id); // Id 由框架自动生成
    }
}