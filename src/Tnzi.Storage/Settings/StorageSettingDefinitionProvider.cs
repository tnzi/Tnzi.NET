namespace Tnzi.Storage.Settings;

/// <summary>
/// Storage 模块内置配置定义 — Upload Limits 组，映射 StorageOptions（配置节 "Storage"）。
/// 收录字段经 FileStorageService（IOptionsMonitor.CurrentValue）运行时热消费。
/// 不收录：EnableMd5Validation（全仓无消费者）；UrlPrefix（被四个单例存储 provider
/// 构造期冻结进 _baseUrl，运行时改不生效 — 待 provider 改为按次取值后回填）。
/// </summary>
public class StorageSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "storage-upload",
            ModuleName = "Storage",
            DisplayName = "Upload Limits",
            I18nKey = $"{I18nBase}.groups.storageUpload",
            Icon = "mdi:cloud-upload-outline",
            Order = 300,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "Storage:MaxFileSize", Label = "Max File Size (bytes)", Type = SettingFieldType.Int, Min = 1,
                    I18nKey = $"{I18nBase}.fields.maxFileSize",
                    DefaultValueAccessor = () => new StorageOptions().MaxFileSize.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "Storage:ImageCompressionQuality", Label = "Image Compression Quality", Type = SettingFieldType.Int, Min = 1, Max = 100,
                    I18nKey = $"{I18nBase}.fields.imageCompressionQuality",
                    DefaultValueAccessor = () => new StorageOptions().ImageCompressionQuality.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
    ];
}
