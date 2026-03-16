using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Results;

namespace Tnzi.Mapster.Tests;

public class ResultMappingExtensionsTests
{
    private readonly TypeAdapterConfig _config = new();
    private readonly ServiceMapper _mapper;

    public ResultMappingExtensionsTests()
    {
        _config.NewConfig<SourceModel, TargetModel>();
        _mapper = new ServiceMapper(new ServiceCollection().BuildServiceProvider(), _config);
    }

    [Fact]
    public void Map_WithSuccessfulResult_MapsDataAndPreservesMetadata()
    {
        using var _ = MapperExtensions.PushMapper(_mapper, _config);
        var result = Result<SourceModel>.Success(new SourceModel { Name = "Alice", Age = 18 }, "ok", 201);

        var mapped = result.Map<SourceModel, TargetModel>();

        Assert.True(mapped.Succeeded);
        Assert.Equal("Alice", mapped.Data?.Name);
        Assert.Equal(18, mapped.Data?.Age);
        Assert.Equal("ok", mapped.Message);
        Assert.Equal(201, mapped.Code);
    }

    [Fact]
    public void Map_WithFailedResult_PreservesFailurePayload()
    {
        using var _ = MapperExtensions.PushMapper(_mapper, _config);
        var errorDetails = new { Field = "Name" };
        var result = Result<SourceModel>.Failure("bad request", 422, "VALIDATION_ERROR", errorDetails);

        var mapped = result.Map<SourceModel, TargetModel>();

        Assert.False(mapped.Succeeded);
        Assert.Equal("bad request", mapped.Message);
        Assert.Equal(422, mapped.Code);
        Assert.Equal("VALIDATION_ERROR", mapped.ErrorCode);
        Assert.Same(errorDetails, mapped.ErrorDetails);
    }

    [Fact]
    public void Map_WithNullData_ReturnsMappingFailure()
    {
        using var _ = MapperExtensions.PushMapper(_mapper, _config);
        var result = Result<SourceModel?>.Success(null);

        var mapped = result.Map<SourceModel?, TargetModel>();

        Assert.False(mapped.Succeeded);
        Assert.Equal(400, mapped.Code);
        Assert.Equal("MAPPING_ERROR", mapped.ErrorCode);
    }

    [Fact]
    public async Task MapAsync_WithTaskResult_MapsSuccessfully()
    {
        using var _ = MapperExtensions.PushMapper(_mapper, _config);
        var resultTask = Task.FromResult(Result<SourceModel>.Success(new SourceModel { Name = "Bob", Age = 30 }));

        var mapped = await resultTask.MapAsync(source =>
            Task.FromResult(new TargetModel { Name = source.Name, Age = source.Age + 1 }));

        Assert.True(mapped.Succeeded);
        Assert.Equal("Bob", mapped.Data?.Name);
        Assert.Equal(31, mapped.Data?.Age);
    }

    [Fact]
    public async Task MapAsync_WithMapperException_ThrowsInDebug()
    {
        using var _ = MapperExtensions.PushMapper(_mapper, _config);
        var result = Result<SourceModel>.Success(new SourceModel { Name = "Error", Age = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            result.MapAsync(_ => Task.FromException<TargetModel>(new InvalidOperationException("boom"))));
    }

    private sealed class SourceModel
    {
        public string Name { get; init; } = string.Empty;
        public int Age { get; init; }
    }

    private sealed class TargetModel
    {
        public string Name { get; init; } = string.Empty;
        public int Age { get; init; }
    }
}
