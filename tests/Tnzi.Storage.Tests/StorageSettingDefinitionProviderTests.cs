namespace Tnzi.Storage.Tests;

/// <summary>
/// StorageOptions 配置中心特性测试 - 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
/// </summary>
public class StorageSettingDefinitionProviderTests
{
    private readonly SettingDefinitionGroup _group =
        RuntimeSettingMetadataExtractor.Extract(typeof(StorageOptions))!;

    [Fact]
    public void Extract_ReturnsNonNull()
    {
        Assert.NotNull(_group);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        Assert.Equal("storage-upload", _group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        Assert.Equal("Storage", _group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        Assert.Equal(300, _group.Order);
    }

    [Fact]
    public void Group_HasSevenFields()
    {
        // MaxFileSize + ImageCompressionQuality（原始）+ EnableMd5Validation / EnableFileReference /
        // AutoGenerateThumbnail / UrlPrefix（新暴露，均已接热消费者：FileStorageService/FileChunkUploadService
        // 经 IOptionsMonitor.CurrentValue 热读，UrlPrefix 经 storage provider 可选 IOptionsMonitor 热读）
        // + AllowAnonymousRead（部署级匿名读开关，FileAccessAuthorizer 经 IOptionsMonitor.CurrentValue 热读）。
        // 缩略图宽高在嵌套 ThumbnailSizeOptions（独立 ConfigSection），不属于本组直接字段。
        Assert.Equal(7, _group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _group.Fields;
        Assert.Contains(fields, f => f.Key == "Storage:MaxFileSize");
        Assert.Contains(fields, f => f.Key == "Storage:ImageCompressionQuality");
        Assert.Contains(fields, f => f.Key == "Storage:EnableMd5Validation");
        Assert.Contains(fields, f => f.Key == "Storage:EnableFileReference");
        Assert.Contains(fields, f => f.Key == "Storage:AutoGenerateThumbnail");
        Assert.Contains(fields, f => f.Key == "Storage:UrlPrefix");
        Assert.Contains(fields, f => f.Key == "Storage:AllowAnonymousRead");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var fields = _group.Fields;
        var maxFileSize = fields.First(f => f.Key == "Storage:MaxFileSize");
        var imageQuality = fields.First(f => f.Key == "Storage:ImageCompressionQuality");

        Assert.Equal(SettingFieldType.Int, maxFileSize.Type);
        Assert.Equal(SettingFieldType.Int, imageQuality.Type);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var fields = _group.Fields;

        var maxFileSize = fields.First(f => f.Key == "Storage:MaxFileSize");
        var imageQuality = fields.First(f => f.Key == "Storage:ImageCompressionQuality");

        Assert.NotNull(maxFileSize.DefaultValueAccessor);
        Assert.Equal((100L * 1024 * 1024).ToString(), maxFileSize.DefaultValueAccessor!());

        Assert.NotNull(imageQuality.DefaultValueAccessor);
        Assert.Equal("85", imageQuality.DefaultValueAccessor!());
    }

    [Fact]
    public void Fields_HaveI18nKeys()
    {
        Assert.All(_group.Fields, f => Assert.NotNull(f.I18nKey));
    }
}
