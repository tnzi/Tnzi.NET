
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
public class StringTrimModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(string))
        {
            return new StringTrimModelBinder();
        }
        return null;
    }
}