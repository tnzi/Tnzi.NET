namespace Tnzi.Storage.Tests;

/// <summary>
/// StorageSettingDefinitionProvider 结构测试 — 验证 provider 注册字段符合配置中心契约
/// </summary>
public class StorageSettingDefinitionProviderTests
{
    private readonly StorageSettingDefinitionProvider _provider = new();

    [Fact]
    public void GetGroups_ReturnsOneGroup()
    {
        var groups = _provider.GetGroups();
        Assert.Single(groups);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("storage-upload", group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("Storage", group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(300, group.Order);
    }

    [Fact]
    public void Group_HasTwoFields()
    {
        // EnableMd5Validation（无消费者）与 UrlPrefix（单例 provider 构造期冻结）已移除
        var group = _provider.GetGroups()[0];
        Assert.Equal(2, group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _provider.GetGroups()[0].Fields;
        Assert.Contains(fields, f => f.Key == "Storage:MaxFileSize");
        Assert.Contains(fields, f => f.Key == "Storage:ImageCompressionQuality");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var fields = _provider.GetGroups()[0].Fields;
        var maxFileSize = fields.First(f => f.Key == "Storage:MaxFileSize");
        var imageQuality = fields.First(f => f.Key == "Storage:ImageCompressionQuality");

        Assert.Equal(SettingFieldType.Int, maxFileSize.Type);
        Assert.Equal(SettingFieldType.Int, imageQuality.Type);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var fields = _provider.GetGroups()[0].Fields;

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
        var fields = _provider.GetGroups()[0].Fields;
        Assert.All(fields, f => Assert.NotNull(f.I18nKey));
    }
}
