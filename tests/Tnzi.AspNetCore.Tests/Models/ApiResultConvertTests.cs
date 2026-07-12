using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Tnzi.AspNetCore.Tests.Models;

/// <summary>
/// 锁定 <see cref="ApiResult{T}"/> 的 <see cref="IConvertToActionResult"/> 契约：
/// 失败信封必须携带真实 HTTP 状态码（与 body.code 一致），而非以往恒 200 的双语义。
/// 单元层验证 Convert() 产物形状；端到端行为见 HttpStatusSemanticsEndToEndTests。
/// </summary>
public class ApiResultConvertTests
{
    [Fact]
    public void Convert_FailureEnvelope_ProducesObjectResultWithRealStatusCode()
    {
        var envelope = ApiResult<string>.Error("not found", 404);

        var actionResult = ((IConvertToActionResult)envelope).Convert();

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        // 真实 HTTP 状态码来自信封 Code
        Assert.Equal(404, objectResult.StatusCode);
        // Value 仍是信封本体 → 内容协商按注册 formatter 序列化信封（连带核查 b）
        Assert.Same(envelope, objectResult.Value);
        // Value is IApiResult → ApiResultWrapperFilter 的提前返回路径成立（连带核查 a）
        Assert.IsAssignableFrom<IApiResult>(objectResult.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(500)]
    public void Convert_MapsEnvelopeCodeToHttpStatus(int code)
    {
        var envelope = ApiResult<string>.Error("failure", code);

        var objectResult = Assert.IsType<ObjectResult>(((IConvertToActionResult)envelope).Convert());

        Assert.Equal(code, objectResult.StatusCode);
    }

    [Fact]
    public void Convert_SuccessEnvelope_Produces200()
    {
        var envelope = ApiResult<string>.Ok("hi");

        var objectResult = Assert.IsType<ObjectResult>(((IConvertToActionResult)envelope).Convert());

        Assert.Equal(200, objectResult.StatusCode);
    }

    [Fact]
    public void Convert_NonGenericEnvelope_InheritsConversion()
    {
        // 非泛型 ApiResult 继承自 ApiResult<object>，自动获得显式接口实现
        var envelope = ApiResult.Error("server error", 500);

        var objectResult = Assert.IsType<ObjectResult>(((IConvertToActionResult)envelope).Convert());

        Assert.Equal(500, objectResult.StatusCode);
        Assert.Same(envelope, objectResult.Value);
    }
}
