namespace Tnzi.Storage.Tests;

/// <summary>
/// StorageOptions 配置中心特性测试 — 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
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
    public void Group_HasTwoFields()
    {
        // EnableMd5Validation（无消费者）与 UrlPrefix（单例 provider 构造期冻结）已移除
        Assert.Equal(2, _group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _group.Fields;
        Assert.Contains(fields, f => f.Key == "Storage:MaxFileSize");
        Assert.Contains(fields, f => f.Key == "Storage:ImageCompressionQuality");
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
