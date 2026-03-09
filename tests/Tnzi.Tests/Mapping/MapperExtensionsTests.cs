using MapsterMapper;
using Tnzi.Mapster;

namespace Tnzi.Tests.Mapping;

public class MapperExtensionsTests
{
    [Fact]
    public void PushMapper_ShouldOverrideMapperWithinScope_AndRestorePreviousMapper()
    {
        var outerMapper = new Mock<IMapper>();
        outerMapper.Setup(x => x.Map<string>(It.IsAny<object>())).Returns("outer");

        var innerMapper = new Mock<IMapper>();
        innerMapper.Setup(x => x.Map<string>(It.IsAny<object>())).Returns("inner");

        MapperExtensions.SetMapper(outerMapper.Object);

        Assert.Equal("outer", new object().MapTo<string>());

        using (MapperExtensions.PushMapper(innerMapper.Object))
        {
            Assert.Equal("inner", new object().MapTo<string>());
        }

        Assert.Equal("outer", new object().MapTo<string>());
    }
}
