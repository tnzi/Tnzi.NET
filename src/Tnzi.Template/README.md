# Tnzi.Template 模板引擎

## 功能
- Razor 模板渲染（字符串/文件）
- YAML front matter 解析（模板元数据）
- 文件系统模板提供者（优先级最高）
- 缓存与热重载（可配置）

## 配置
```json
{
  "Template": {
    "TemplateRootPath": "Templates",
    "TemplateExtension": ".cshtml",
    "EnableCache": true,
    "EnableHotReload": false,
    "EnableFileSystemTemplates": true
  }
}
```

> 应用层可通过环境变量或不同 appsettings 覆盖 `TemplateRootPath`，指向发布输出内的模板目录。

## 目录约定
```
Templates/
  ├── {Module}/
  │   └── {Category}/
  │       └── {Template}.cshtml
  └── Layouts/
      └── {Category}/
          └── _{Layout}.cshtml
```

## 文件格式示例

文件 = 可选的 YAML front matter（开头的 `--- ... ---` 块）+ Razor 正文。

```cshtml
---
subject: Welcome to our site
layout: DefaultEmail
description: Welcome email
metadata:
  type: Email
  version: "1.0"
---
<h2>Welcome @Model.UserName!</h2>
<p>Thanks for joining @Model.SiteName.</p>
```

布局文件：
```cshtml
---
description: Default email layout
isDefault: true
metadata:
  type: Email
---
<!DOCTYPE html>
<html>
<head>
    <title>@Model.Subject</title>
    <meta charset="utf-8" />
</head>
<body>
    <div>@Html.Raw(Model.Content)</div>
</body>
</html>
```

### 两条约束（都是实测的，写错不会报错只会渲染出怪东西）

**1. front matter 的键名是 camelCase，且大小写敏感。**
`subject` / `layout` / `description` / `type` / `isDefault` / `metadata`。写成 `Subject:` 不会报错，
也不会有警告 —— 该键被静默忽略，元数据为空。框架自带的模板（`Tnzi.Hosting/Templates/`）就是这个写法，
可直接对照。

**2. 没有 `@model` 强类型指令。** 模型是动态的（`SafeDynamicObject`），直接写 `@Model.UserName` 即可；
访问不存在的字段返回 null 而不抛异常。写 `@model SomeType` 会导致模板编译失败
（`error CS0103: The name 'model' does not exist in the current context`）。

### front matter 在两条路径上的处理

| 路径 | 行为 |
|---|---|
| 导入模板存储（`ITemplateStoreService` / `ILayoutStoreService`，含文件系统后备） | 解析元数据：`subject` → `SubjectTemplate`、`layout` → `DefaultLayoutName`、`isDefault` → 布局默认标记等 |
| 直接按文件渲染（`ITemplateEngine.RenderFromFileAsync` / `RenderFromNameAsync`） | **剥离后丢弃**：不出现在输出里，也不解析、不注入 `@Model` |

两条路径拿到的**正文完全一致**（同一套 front matter 识别规则）。差别只在元数据：引擎的文件渲染路径
是"渲染这个文件"，不是"加载一个模板定义"，所以它不会替你解析 `layout:` 去套布局 —— 布局由
`layoutPath` 参数或 `DefaultLayoutPath` 配置显式指定。需要元数据驱动（`subject` 渲染、`layout:`
自动生效）的消费方请走 `ITemplateRenderService`。

> 正文以 `---` 开头的文件会被当作带 front matter。正文**中间**的 `---`（分隔线）不受影响。

## 使用
```csharp
// 注册（在启动时）
services.AddTnziTemplate(builder.Configuration);

// 渲染
var content = await templateEngine.RenderAsync("Hello @Model.Name", new { Name = "User" });
var fromFile = await templateEngine.RenderFromFileAsync("Templates/Notification/Email/UserWelcome.cshtml", model);
```

## 发布与复制

### 重要：模板文件编译配置

**模板文件（`.cshtml`）必须从编译中排除**，否则会导致编译错误（如 `The name 'Raw' does not exist in the current context`）。

**原因**：
- 模板文件使用 Razor 语法，不是标准 C# 代码
- `Raw`、`HtmlEncode` 等方法定义在 `TemplateBase` 基类中，由 `RazorTemplateEngine` 在运行时动态注入
- 编译时 IDE 和编译器看不到这些方法，因此会报错

**正确的配置方式**：

在应用项目的 `.csproj` 文件中：

```xml
<ItemGroup>
  <!-- 排除模板文件不被编译（模板文件由 RazorTemplateEngine 在运行时处理） -->
  <Compile Remove="Templates\**\*.cshtml" />
  <Content Remove="Templates\**\*.cshtml" />
</ItemGroup>

<ItemGroup>
  <!-- 将模板文件标记为内容文件，复制到输出目录，但不编译 -->
  <None Include="Templates\**\*.cshtml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**配置说明**：
- `Compile Remove`: 从编译中排除所有 `.cshtml` 文件
- `Content Remove`: 从内容文件中移除（避免重复）
- `None Include`: 将模板文件标记为内容文件，复制到输出目录但不参与编译

这样配置后：
- ✅ 模板文件会被复制到输出目录（运行时可用）
- ✅ 不会被 C# 编译器处理（避免编译错误）
- ✅ IDE 不会对模板文件进行语法检查（避免设计时错误）
- ✅ `RazorTemplateEngine` 可以在运行时正确加载和渲染模板

### 复制到输出目录

确保 `TemplateRootPath` 与发布输出中的实际路径一致。

