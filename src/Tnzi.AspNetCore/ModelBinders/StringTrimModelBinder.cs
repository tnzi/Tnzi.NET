namespace Tnzi.AspNetCore.ModelBinders;

/// <summary>
/// 字符串自动Trim的ModelBinder
/// </summary>
public class StringTrimModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext.ModelType != typeof(string))
        {
            return Task.CompletedTask;
        }

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        var value = valueProviderResult.FirstValue;
        if (value != null)
        {
            bindingContext.Result = ModelBindingResult.Success(value.Trim());
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 字符串Trim的ModelBinderProvider
/// </summary>
/// <remarks>
/// <para>
/// 只接管<strong>值提供者能服务的来源</strong>：路由、查询串、表单（以及未标注来源、
/// 由 MVC 按这三者依次查找的字符串）。<see cref="StringTrimModelBinder"/> 只会从
/// <c>ValueProvider</c> 取值，而请求体、请求头与 <c>[ModelBinder(typeof(...))]</c>
/// 指定的自定义 binder 都不在值提供者里 —— 这些来源一旦被本 provider 抢走，
/// 参数就永远是 <c>null</c>，而编译期与启动期都不会有任何提示。
/// </para>
/// <para>
/// 判据是「这个来源能不能从 Form / Path / Query 拿数据」而不是「是不是 Body」：
/// 来源集合是开放的（<c>Header</c>、<c>Services</c>、<c>Custom</c>、<c>FormFile</c>，
/// 消费方还能自定义），逐个列举拒绝项迟早漏一个，而列举接受项恰好就是值提供者的覆盖面。
/// </para>
/// </remarks>
public class StringTrimModelBinderProvider : IModelBinderProvider
{
    private static readonly BindingSource[] ValueProviderSources =
    [
        BindingSource.Form,
        BindingSource.Path,
        BindingSource.Query
    ];

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        Check.NotNull(context);

        if (context.Metadata.ModelType != typeof(string))
        {
            return null;
        }

        return CanBindFromValueProviders(context.BindingInfo.BindingSource)
            ? new StringTrimModelBinder()
            : null;
    }

    /// <summary>
    /// 未标注来源（<c>null</c>）按 MVC 默认走值提供者；标注了来源的只在它接受
    /// Form / Path / Query 之一时接管（<see cref="BindingSource.ModelBinding"/> 复合来源也在内）。
    /// </summary>
    public static bool CanBindFromValueProviders(BindingSource? source)
    {
        if (source == null)
        {
            return true;
        }

        return ValueProviderSources.Any(source.CanAcceptDataFrom);
    }
}
