#if ENABLE_AZURE_STORAGE
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Storage.Options;
using Tnzi.Storage.Providers;
using Xunit;

namespace Tnzi.Storage.Tests;

public class AzureBlobStorageTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<AzureBlobStorage>> _mockLogger;

    public AzureBlobStorageTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<AzureBlobStorage>>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new AzureBlobStorage(null!));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConnectionStringIsEmpty()
    {
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "",
            ContainerName = "test-container"
        };
        Assert.Throws<ArgumentException>(() => new AzureBlobStorage(options, null, null));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenContainerNameIsEmpty()
    {
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = ""
        };
        Assert.Throws<ArgumentException>(() => new AzureBlobStorage(options, null, null));
    }

    [Fact]
    public void Constructor_ShouldInitialize_WhenOptionsAreValid()
    {
        // This test will fail if it tries to connect to Azure in constructor
        // AzureBlobStorage constructor does create BlobServiceClient but usually doesn't connect until used
        // However, "UseDevelopmentStorage=true" format is valid for parsing

        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test-container"
        };

        var storage = new AzureBlobStorage(options, _mockConfiguration.Object, _mockLogger.Object);
        Assert.Equal("Azure", storage.ProviderName);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenFileNameIsNullOrEmpty()
    {
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test-container"
        };
        var storage = new AzureBlobStorage(options, _mockConfiguration.Object, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync(null!, new MemoryStream()));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync("", new MemoryStream()));
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenStreamIsNull()
    {
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test-container"
        };
        var storage = new AzureBlobStorage(options, _mockConfiguration.Object, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.UploadAsync("test.txt", null!));
    }

}
#endif
