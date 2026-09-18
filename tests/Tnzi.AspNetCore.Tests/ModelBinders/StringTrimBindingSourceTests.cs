using Microsoft.AspNetCore.Mvc.ModelBinding;
using Tnzi.AspNetCore.ModelBinders;

namespace Tnzi.AspNetCore.Tests.ModelBinders;

/// <summary>
/// <see cref="StringTrimModelBinderProvider.CanBindFromValueProviders"/> 的真值表：
/// 值提供者只覆盖 Form / Path / Query，其它来源一律让给内置 provider。
/// 管线测试（<see cref="StringTrimModelBinderTests"/>）证明的是最终效果，
/// 这一条单独钉住来源判定 —— provider 的位置被人挪回第 0 位时，只有它还挡得住。
/// </summary>
public class StringTrimBindingSourceTests
{
    public static IEnumerable<object?[]> Sources()
    {
        yield return [null, true];
        yield return [BindingSource.Query, true];
        yield return [BindingSource.Path, true];
        yield return [BindingSource.Form, true];
        yield return [BindingSource.ModelBinding, true];
        yield return [BindingSource.Body, false];
        yield return [BindingSource.Header, false];
        yield return [BindingSource.Custom, false];
        yield return [BindingSource.Services, false];
        yield return [BindingSource.FormFile, false];
        yield return [BindingSource.Special, false];
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void CanBindFromValueProviders_MatchesValueProviderCoverage(BindingSource? source, bool expected)
    {
        Assert.Equal(expected, StringTrimModelBinderProvider.CanBindFromValueProviders(source));
    }
}
