# @tnzi/shadcn

> Tnzi UI components based on shadcn-vue for desktop SPA applications.

> 桌面端 UI 实现包（Vue + shadcn-vue）。

## 安装

```bash
pnpm add @tnzi/shadcn
```

## 使用

### 插件安装

```typescript
// main.ts
import { createApp } from "vue";
import TnziUi from "@tnzi/shadcn";

const app = createApp(App);
app.use(TnziUi);
app.mount("#app");
```

插件选项：

```typescript
app.use(TnziUi, {
    locale: "zh-CN",
    registerComponents: true,
    registerIntegrations: true, // 将 message/dialog 适配器注册到 @tnzi/core
});
```

### 组件使用

```vue
<template>
    <div>
        <!-- 认证组件 -->
        <TLoginForm @submit="handleLogin" />
        <TRegisterForm @submit="handleRegister" />

        <!-- 表格组件 -->
        <TDataTable :data="users" :columns="columns" />

        <!-- 表单组件 -->
        <TDynamicForm v-model="formData" :fields="formFields" />
        <TSearchForm v-model="searchData" @search="handleSearch" />

        <!-- 卡片组件 -->
        <TUserCard :user="currentUser" />
        <TStatCard title="Total Users" :value="1234" trend="{12.5}" />

        <!-- 布局组件 -->
        <TAdminLayout :sidebar-items="menuItems">
            <template #header>
                <THeader :user="user" />
            </template>
            <TBreadcrumb :items="breadcrumbs" />
        </TAdminLayout>
    </div>
</template>
```

### Hooks 使用

```typescript
import { useShadcnTheme, useShadcnMessage, useShadcnDialog } from "@tnzi/shadcn";

const { isDark, toggleTheme } = useShadcnTheme();
const message = useShadcnMessage();
const dialog = useShadcnDialog();

message.success("操作成功");
```

## 适配器注册规范

- `@tnzi/core` 维护运行时 adapter registry。
- `@tnzi/shadcn` 插件默认在 `install` 时注册 message/dialog 适配器。
- 业务代码应优先通过 `@tnzi/core/adapters` 消费适配器，而不是直接耦合具体 UI 库。

### Auto-import 配置

```typescript
// vite.config.ts
import Components from "unplugin-vue-components/vite";
import { TnziUiResolver } from "@tnzi/shadcn/resolvers";

export default {
    plugins: [
        Components({
            resolvers: [TnziUiResolver({ prefix: "T" })],
        }),
    ],
};
```

## 组件列表与架构约束

1. `src/components/ui/*`：内部 primitives（基于 shadcn-vue），仅对内复用。
2. `src/components/*`：对外 `T*` 组件（基础组件 + 业务组合组件）。
3. 业务项目默认只使用 `T*`，不直接依赖内部 primitives。
4. 所有 `T*` 优先遵循 `@tnzi/core/components` 契约。

### 认证组件

- `TLoginForm` - 登录表单
- `TRegisterForm` - 注册表单
- `TPasswordReset` - 密码重置

### 数据展示组件

- `TDataTable` - 数据表格
- `TDataList` - 数据列表（Web 分页交互，契约层 `IWebDataListProps/Emits`）
- `TUserCard` - 用户卡片
- `TStatCard` - 统计卡片

### 表单组件

- `TForm` - 通用表单容器（契约层 `IFormProps/IFormEmits`）
- `TDynamicForm` - 动态表单
- `TSearchForm` - 搜索表单

### 布局组件

- `TAdminLayout` - 管理后台布局
- `TSidebar` - 侧边栏
- `THeader` - 页头
- `TBreadcrumb` - 面包屑

### 导航组件

- `TMenu` - 桌面菜单
- `TNavBar` - 顶部导航栏
- `TTabBar` - 标签导航栏

## Playground

```bash
pnpm -C packages/web dev
```

## 内置依赖

- `shadcn-vue` ^0.9.0
- `clsx` ^2.1.0
- `tailwind-merge` ^2.5.0
- `class-variance-authority` ^0.7.0

## License

MIT
