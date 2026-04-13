# UI 包开发规范 (UI Package Development Guide)

> 本文档定义了 `@tnzi/{ui|ui-admin|ai|mobile}` UI 包的开发规范和最佳实践，适用于新建和维护 UI 包。

## 目录

1. [核心原则](#核心原则)
2. [目录结构](#目录结构)
3. [Playground 规范](#playground-规范)
4. [适配器开发](#适配器开发)
5. [Store 包装](#store-包装)
6. [组件开发](#组件开发)
7. [Plugin 注册](#plugin-注册)
8. [导出规范](#导出规范)
9. [命名约定](#命名约定)
10. [样式规范](#样式规范)
11. [测试规范](#测试规范)
12. [新包创建清单](#新包创建清单)

---

## 核心原则

### 1. 业务逻辑在 core，UI 包只做视图适配

```
@tnzi/core              → 100% 业务逻辑 (state managers, headless controllers, API, types)
@tnzi/{ui-package}      → 纯视图层 (adapters, Pinia wrappers, Vue components)
```

**禁止** 在 UI 包中编写业务逻辑（API 调用、状态计算、验证规则等）。
所有业务逻辑必须在 `@tnzi/core` 中实现。

### 2. 薄封装原则

UI 包中的代码应尽可能薄：

- **Adapter**: 5-30 行，仅做 API 转译
- **Pinia Store**: 10-20 行/store，委托 `core/state` 管理器
- **组件**: 纯视图 + 事件绑定，复杂交互逻辑使用 `core/headless` 控制器

### 3. 统一接口

所有 UI 包实现相同的 core 接口：

- `MessageAdapter` — 消息/Toast
- `DialogAdapter` — 对话框
- `ThemeAdapter` — 主题切换
- `ILoginFormProps` / `ILoginFormEmits` — 组件接口
- `useAuthStore()` / `useUserStore()` / `useAppStore()` — Store 接口

用户切换 UI 框架时，只需更换 import 路径，业务代码无需修改。

---

## 目录结构

### 标准 UI 包目录

```
packages/{ui-package}/
├── package.json
├── tsconfig.json
├── vite.config.ts                # lib 模式构建配置
├── src/
│   ├── index.ts                  # 主入口 (re-export all)
│   ├── plugin.ts                 # Vue 插件 (createTnzi{Xxx})
│   ├── adapters/                 # core adapter 实现
│   │   ├── index.ts              # 统一导出
│   │   ├── message.ts            # MessageAdapter 实现
│   │   ├── dialog.ts             # DialogAdapter 实现
│   │   └── theme.ts              # ThemeAdapter 实现
│   ├── stores/                   # Pinia store 包装
│   │   ├── index.ts              # 统一导出
│   │   ├── auth.ts               # useAuthStore (包装 AuthStateManager)
│   │   ├── user.ts               # useUserStore (包装 UserStateManager)
│   │   └── app.ts                # useAppStore (包装 AppStateManager)
│   ├── components/               # T* Vue 组件
│   │   ├── index.ts              # 统一导出
│   │   ├── auth/
│   │   │   └── TLoginForm.vue
│   │   ├── data/
│   │   │   ├── TDataTable.vue
│   │   │   └── TDataList.vue
│   │   ├── form/
│   │   │   ├── TForm.vue
│   │   │   └── TSearchForm.vue
│   │   └── layout/
│   │       └── TAdminLayout.vue
│   ├── composables/              # UI 框架特定的 hooks
│   │   ├── index.ts
│   │   └── useTheme.ts
│   ├── resolvers/                # unplugin-vue-components 解析器
│   │   └── index.ts
│   └── styles/                   # 全局样式 (可选)
│       └── index.css
├── playground/                   # Playground 演示应用 (必须)
│   ├── package.json
│   ├── vite.config.ts
│   ├── index.html
│   └── src/
│       ├── main.ts
│       └── App.vue               # 大杂烩展示页
└── __tests__/                    # 测试文件
    ├── adapters/
    ├── stores/
    └── components/
```

### 必须文件

每个 UI 包 **必须** 包含以下文件：

| 文件 | 说明 |
| --- | --- |
| `package.json` | 包配置，依赖 `@tnzi/core` |
| `tsconfig.json` | TypeScript 配置，引用 core |
| `vite.config.ts` | 构建配置 (lib 模式) |
| `src/index.ts` | 主入口 |
| `src/plugin.ts` | Vue 插件 |
| `src/adapters/message.ts` | MessageAdapter 实现 |
| `src/adapters/dialog.ts` | DialogAdapter 实现 |
| `src/adapters/theme.ts` | ThemeAdapter 实现 |
| `src/resolvers/index.ts` | 组件解析器 |
| `playground/` | Playground 演示应用（见下节） |

---

## Playground 规范

### Playground 作用

每个 UI 包 **必须** 提供 `playground/` 目录，用于：

1. **功能展示** — 展示所有 T* 组件和适配器
2. **开发调试** — 实时预览组件开发效果
3. **交互测试** — 测试适配器、Store、组件的交互行为
4. **文档补充** — 作为可运行的示例代码

**注意：** `@tnzi/core` 是纯逻辑层，无 UI 可展示，因此 **不需要** playground。

### Playground 目录结构

```
packages/{ui-package}/playground/
├── package.json           # playground 独立依赖
├── vite.config.ts         # Vite 配置
├── index.html             # HTML 入口
├── tsconfig.json          # TypeScript 配置（可选）
├── .gitignore             # 忽略 node_modules, dist
└── src/
    ├── main.ts            # 应用入口
    └── App.vue            # 大杂烩展示页
```

### Playground 必要文件

#### 1. package.json

```json
{
  "name": "@tnzi/{ui-package}-playground",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "@tnzi/{ui-package}": "workspace:*",
    "@tnzi/core": "workspace:*",
    "vue": "^3.5.0",
    "pinia": "^2.2.0",
    "{ui-library}": "^x.x.x"  // naive-ui / vant / 等
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^5.0.0",
    "typescript": "^5.9.3",
    "vite": "^5.4.0"
  }
}
```

#### 2. vite.config.ts

```typescript
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 3004,  // shadcn: 3002, vant: 3003, naive-ui: 3004
  },
  resolve: {
    alias: {
      '@tnzi/core': '../../core/src',  // 开发时指向源码
    },
  },
});
```

#### 3. src/main.ts

```typescript
import { createApp } from 'vue';
import { createPinia } from 'pinia';
import Tnzi{Package} from '@tnzi/{ui-package}';
import App from './App.vue';

const app = createApp(App);

app.use(createPinia());
app.use(Tnzi{Package});

app.mount('#app');
```

#### 4. src/App.vue — 大杂烩展示页

**设计原则：**
- **单页集中展示** — 不使用路由，所有功能在一个页面
- **分区域组织** — 使用卡片/面板区分不同功能模块
- **交互式演示** — 每个功能提供按钮触发，显示实时结果
- **参考示例：** `packages/mobile/playground/src/App.vue`

**必须展示的内容：**

1. **适配器示例**
   - MessageAdapter (info/success/warning/error 消息)
   - DialogAdapter (alert/confirm 对话框)
   - ThemeAdapter (主题切换：light/dark/system)

2. **Pinia Stores**
   - useAuthStore (登录/登出、Token 状态)
   - useUserStore (用户信息、偏好设置)
   - useAppStore (主题、语言、加载状态)

3. **T* 业务组件**
   - 已开发的所有 T* 组件（TLoginForm, TDataTable 等）
   - 每个组件提供交互示例

4. **原生 UI 框架组件**
   - 展示该 UI 框架的代表性组件（证明框架正常工作）
   - 示例：
     - shadcn: Button, Card, Form, Table
     - vant: Button, Cell, Card, NavBar
     - naive-ui: Button, Card, Form, DataTable

**代码示例（结构）：**

```vue
<template>
  <div class="playground">
    <header>
      <h1>@tnzi/{ui-package} Playground</h1>
      <button @click="toggleTheme">切换主题</button>
    </header>

    <!-- 1. 适配器示例 -->
    <section class="section">
      <h2>适配器示例</h2>
      <div class="buttons">
        <button @click="showMessage">消息</button>
        <button @click="showDialog">对话框</button>
      </div>
    </section>

    <!-- 2. Pinia Stores -->
    <section class="section">
      <h2>Pinia Stores</h2>
      <div>{{ authStore.isAuthenticated ? '已登录' : '未登录' }}</div>
      <button @click="authStore.login(...)">登录</button>
    </section>

    <!-- 3. T* 业务组件 -->
    <section class="section">
      <h2>T* 业务组件</h2>
      <TLoginForm @submit="onLogin" />
      <TDataTable :data="tableData" :columns="columns" />
    </section>

    <!-- 4. 原生组件 -->
    <section class="section">
      <h2>原生组件示例</h2>
      <!-- Vant: <van-button>, <van-card> -->
      <!-- Naive UI: <n-button>, <n-card> -->
    </section>
  </div>
</template>

<script setup lang="ts">
import { useAuthStore, useAppStore } from '@tnzi/{ui-package}';
import { ref } from 'vue';

const authStore = useAuthStore();
const appStore = useAppStore();

function toggleTheme() {
  appStore.setTheme(appStore.theme === 'light' ? 'dark' : 'light');
}

function showMessage() {
  // 使用适配器显示消息
}
// ...
</script>
```

### Playground 开发指南

1. **启动 Playground**
   ```bash
   cd packages/{ui-package}/playground
   pnpm dev
   ```

2. **构建验证**
   ```bash
   pnpm build
   pnpm preview
   ```

3. **迭代开发流程**
   - 开发新组件 → 在 playground/App.vue 中添加演示
   - 修改适配器 → 在 playground 中测试效果
   - 添加新功能 → 在 playground 中展示用法

4. **最佳实践**
   - 保持 App.vue 简洁，使用计算属性和组合式函数
   - 使用 `<section class="section">` 划分功能区域
   - 添加交互按钮，避免纯静态展示
   - 显示状态数据（如 store 状态、组件 emit 事件）

### Playground 示例对比

| 包 | Playground 质量 | 改进建议 |
|----|----------------|---------|
| vant | ✅ 优秀 | 已完整展示所有功能，作为模板 |
| naive-ui | ✅ 良好 | 新创建，功能完整 |
| shadcn | ⚠️ 需改进 | 目前仅测试 i18n，需补充适配器/Store/组件展示 |

**参考模板：** `packages/mobile/playground/src/App.vue` （最佳实践）

---

## 适配器开发

### 适配器接口 (定义在 core)

```typescript
// @tnzi/core/adapters
export interface MessageAdapter {
  info(content: string, options?: MessageOptions): void;
  success(content: string, options?: MessageOptions): void;
  warning(content: string, options?: MessageOptions): void;
  error(content: string, options?: MessageOptions): void;
  loading(content: string, options?: MessageOptions): () => void;
}

export interface DialogAdapter {
  alert(message: string, options?: DialogOptions): Promise<void>;
  confirm(message: string, options?: DialogOptions): Promise<boolean>;
  prompt(message: string, options?: DialogOptions): Promise<string | null>;
}

export interface ThemeAdapter {
  applyTheme(mode: 'light' | 'dark' | 'system'): void;
  getResolvedTheme(): 'light' | 'dark';
  onSystemThemeChange?(callback: (theme: 'light' | 'dark') => void): () => void;
  setPrimaryColor?(color: string): void;
}
```

### 实现模板

每个适配器导出一个 `create{Framework}{Feature}Adapter()` 工厂函数：

```typescript
// packages/{ui-package}/src/adapters/message.ts
import type { MessageAdapter } from '@tnzi/core';
import { showToast, showSuccessToast, showFailToast, showLoadingToast } from 'xxx-ui';

export function createXxxMessageAdapter(): MessageAdapter {
  return {
    info(content) {
      showToast(content);
    },
    success(content) {
      showSuccessToast(content);
    },
    warning(content) {
      showToast({ message: content, type: 'warning' });
    },
    error(content) {
      showFailToast(content);
    },
    loading(content) {
      const instance = showLoadingToast({ message: content, duration: 0 });
      return () => instance.close(); // 必须返回关闭函数
    },
  };
}
```

### 适配器开发规则

1. **必须实现接口中的全部方法**，不得遗漏
2. **`loading()` 必须返回关闭函数** `() => void`
3. **`confirm()` 必须返回 `Promise<boolean>`**，用户取消时 resolve `false`，不要 reject
4. **`prompt()` 用户取消时返回 `null`**
5. **无法实现的方法回退到原生 API**（如 `window.confirm`），不要抛出异常
6. **不要在适配器中引入业务逻辑**（如 i18n 翻译、条件判断）
7. **Naive UI 特殊处理**：`useMessage()` / `useDialog()` 必须在 Vue setup 上下文中调用，适配器需接受 API 实例作为参数

```typescript
// Naive UI 适配器需接受 API 实例
export function createNaiveMessageAdapter(
  messageApi: ReturnType<typeof useMessage>
): MessageAdapter {
  return {
    info(content) { messageApi.info(content); },
    // ...
  };
}
```

---

## Store 包装

### 架构设计

Pinia Store 仅做薄包装，委托 `core/state` 管理器：

```
core/state/AuthStateManager  ←  业务逻辑 (login, logout, token refresh...)
      ↑ 委托
ui/stores/useAuthStore        ←  Pinia 包装 (响应式桥接 + devtools)
```

### Store 包装模板

```typescript
// packages/{ui-package}/src/stores/auth.ts
import { defineStore } from 'pinia';
import { AuthStateManager } from '@tnzi/core/state';
import type { StateDeps } from '@tnzi/core/state';

let _manager: AuthStateManager | null = null;

function getManager(deps: StateDeps): AuthStateManager {
  if (!_manager) {
    _manager = new AuthStateManager(deps);
  }
  return _manager;
}

export const useAuthStore = defineStore('auth', () => {
  // 获取 core state manager（由 plugin 注入依赖）
  const manager = getManager(inject('tnzi:deps')!);

  // 直接暴露 manager 的响应式属性和方法
  // manager 已经通过 reactive(this) 启用响应式
  return {
    // 状态 — 直接转发
    get isAuthenticated() { return manager.isAuthenticated; },
    get accessToken() { return manager.accessToken; },
    get user() { return manager.user; },
    get error() { return manager.error; },

    // Getters — 直接转发
    get isLoggedIn() { return manager.isLoggedIn; },
    get userName() { return manager.userName; },
    get displayName() { return manager.displayName; },

    // Actions — 直接委托
    login: manager.login.bind(manager),
    logout: manager.logout.bind(manager),
    refreshAccessToken: manager.refreshAccessToken.bind(manager),
    restoreAuth: manager.restoreAuth.bind(manager),
  };
});
```

### Store 包装规则

1. **每个 store 不超过 30 行**，如果超过说明业务逻辑没有下沉到 core
2. **禁止在 store 中编写 API 调用**，所有 API 调用在 `core/state` 中
3. **禁止在 store 中编写状态计算逻辑**，所有 getters 委托 manager
4. **store 只做两件事**：
   - 桥接 `core/state` manager 到 Pinia 响应式系统
   - 为 Vue Devtools 提供可视化入口
5. **manager 实例应延迟创建**，在首次使用时初始化

---

## 组件开发

### 组件接口 (定义在 core)

```typescript
// @tnzi/core/components/auth.ts
export interface ILoginFormProps {
  loading?: boolean;
  disabled?: boolean;
  showRememberMe?: boolean;
  showForgotPassword?: boolean;
  title?: string;
}

export interface ILoginFormEmits {
  (e: 'submit', data: { userName: string; password: string; rememberMe: boolean }): void;
  (e: 'forgotPassword'): void;
}
```

### 组件实现模板

```vue
<!-- packages/{ui-package}/src/components/auth/TLoginForm.vue -->
<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { ILoginFormProps, ILoginFormEmits } from '@tnzi/core/components';
// 导入 UI 框架原子组件
import { NForm, NFormItem, NInput, NButton, NCheckbox } from 'naive-ui';

const { t } = useI18n();

const props = withDefaults(defineProps<ILoginFormProps>(), {
  showRememberMe: true,
  showForgotPassword: true,
  loading: false,
});

const emit = defineEmits<ILoginFormEmits>();

// 本地表单状态
const username = ref('');
const password = ref('');
const rememberMe = ref(false);

const handleSubmit = () => {
  emit('submit', {
    userName: username.value,
    password: password.value,
    rememberMe: rememberMe.value,
  });
};
</script>

<template>
  <!-- 使用 UI 框架原子组件构建视图 -->
  <NForm @submit.prevent="handleSubmit">
    <NFormItem :label="t('auth.username')">
      <NInput v-model:value="username" :disabled="disabled" />
    </NFormItem>
    <NFormItem :label="t('auth.password')">
      <NInput v-model:value="password" type="password" :disabled="disabled" />
    </NFormItem>
    <NFormItem v-if="showRememberMe">
      <NCheckbox v-model:checked="rememberMe">{{ t('auth.rememberMe') }}</NCheckbox>
    </NFormItem>
    <NButton type="primary" attr-type="submit" block :loading="loading">
      {{ t('auth.login') }}
    </NButton>
  </NForm>
</template>
```

### 使用 Headless 控制器的组件

```vue
<!-- packages/{ui-package}/src/components/data/TDataTable.vue -->
<script setup lang="ts">
import { DataQueryController } from '@tnzi/core/headless';
import type { ITableColumn, IPaginationConfig } from '@tnzi/core/types/shared-ui';
import { NDataTable, NPagination } from 'naive-ui';

// Props 直接内联定义于 defineProps (Vue 3 泛型推断最佳实践)
const props = defineProps<{
  data: unknown[];
  columns: ITableColumn[];
  controller?: DataQueryController;
  fetchFn?: () => Promise<unknown>;
  pageSize?: number;
  immediate?: boolean;
}>();
const emit = defineEmits<{
  sort: [field: string, order: 'asc' | 'desc'];
  pageChange: [pageIndex: number, pageSize: number];
}>();

// 接收或创建 headless 控制器
const controller = props.controller ?? new DataQueryController({
  fetchFn: props.fetchFn!,
  pageSize: props.pageSize ?? 20,
  immediate: props.immediate ?? true,
});

// 视图直接绑定控制器的响应式属性
// controller 已通过 reactive(this) 启用响应式
</script>

<template>
  <div>
    <NDataTable
      :data="controller.items"
      :loading="controller.isLoading"
      :columns="columns"
      @update:sorter="({ columnKey, order }) => controller.changeSort(columnKey, order)"
    />
    <NPagination
      :page="controller.pagination.pageIndex"
      :page-size="controller.pagination.pageSize"
      :item-count="controller.pagination.totalCount"
      @update:page="controller.changePage"
      @update:page-size="controller.changePageSize"
    />
  </div>
</template>
```

### 组件开发规则

1. **Props/Emits 接口必须定义在 `@tnzi/core/components`**
2. **组件文件名使用 `T` 前缀**: `TLoginForm.vue`, `TDataTable.vue`
3. **组件内不调用 API**，通过 props 接收数据或使用 headless 控制器
4. **组件内不包含业务逻辑**，只做视图渲染和事件转发
5. **使用 `useI18n()` 获取翻译函数**，不硬编码文字
6. **使用 UI 框架原子组件**构建视图（`NButton`, `NInput` 等），不自行实现基础 UI
7. **Headless 控制器可通过 props 注入**，也可在组件内创建
8. **每个功能域一个子目录**: `components/auth/`, `components/data/`, `components/form/`

---

## Plugin 注册

### Plugin 模板

```typescript
// packages/{ui-package}/src/plugin.ts
import type { App, Plugin } from 'vue';
import { setMessageAdapter, setDialogAdapter } from '@tnzi/core';
import { createXxxMessageAdapter } from './adapters/message';
import { createXxxDialogAdapter } from './adapters/dialog';
import { createXxxThemeAdapter } from './adapters/theme';

export interface TnziXxxOptions {
  /** 注册全局 T* 组件 (default: true) */
  registerComponents?: boolean;
  /** 注册 core 适配器 (default: true) */
  registerAdapters?: boolean;
}

export function createTnziXxx(options: TnziXxxOptions = {}): Plugin {
  const {
    registerComponents = true,
    registerAdapters = true,
  } = options;

  return {
    install(app: App) {
      // 1. 注册适配器
      if (registerAdapters) {
        setMessageAdapter(createXxxMessageAdapter());
        setDialogAdapter(createXxxDialogAdapter());
        // ThemeAdapter 注册方式可能不同，按需处理
      }

      // 2. 注册全局组件
      if (registerComponents) {
        // 按需导入，避免全量打包
        const components = import.meta.glob('./components/**/T*.vue', { eager: true });
        for (const [path, module] of Object.entries(components)) {
          const name = path.match(/\/(T\w+)\.vue$/)?.[1];
          if (name) {
            app.component(name, (module as any).default);
          }
        }
      }
    },
  };
}
```

### Plugin 规则

1. **工厂函数命名**: `createTnzi{Framework}` — `createTnziShadcn`, `createTnziMobile`, `createTnziNaiveUi`
2. **必须返回 Vue Plugin 对象** (`{ install(app: App) }`)
3. **Options 中的布尔开关默认为 `true`**（开箱即用）
4. **适配器注册必须在 install 中完成**
5. **组件注册使用 `app.component()`**，支持按需和全量两种模式
6. **默认导出 plugin 工厂函数**

```typescript
// src/index.ts
export { createTnziNaiveUi } from './plugin';
export type { TnziNaiveUiOptions } from './plugin';

// 默认导出
import { createTnziNaiveUi } from './plugin';
export default createTnziNaiveUi;
```

---

## 导出规范

### 主入口 (index.ts)

```typescript
// src/index.ts
// Plugin
export { createTnziXxx } from './plugin';
export type { TnziXxxOptions } from './plugin';
export { createTnziXxx as default } from './plugin';

// Adapters — 供高级用户单独使用
export { createXxxMessageAdapter } from './adapters/message';
export { createXxxDialogAdapter } from './adapters/dialog';
export { createXxxThemeAdapter } from './adapters/theme';

// Stores
export { useAuthStore } from './stores/auth';
export { useUserStore } from './stores/user';
export { useAppStore } from './stores/app';

// Components
export { TLoginForm } from './components/auth/TLoginForm.vue';
export { TDataTable } from './components/data/TDataTable.vue';
// ...
```

### package.json exports

```json
{
  "exports": {
    ".": {
      "import": "./dist/index.mjs",
      "types": "./dist/index.d.ts"
    },
    "./resolvers": {
      "import": "./dist/resolvers.mjs",
      "types": "./dist/resolvers.d.ts"
    },
    "./style.css": "./dist/style.css"
  }
}
```

### 导出规则

1. **主入口导出所有公共 API**
2. **resolvers 单独导出**（构建工具用，不应包含 Vue 运行时代码）
3. **CSS 样式文件单独导出**（如有）
4. **内部文件不暴露**（`adapters/`, `stores/`, `components/` 仅通过主入口访问）
5. **类型导出使用 `export type`**

---

## 命名约定

### 统一命名规则

| 类别 | 格式 | 示例 |
| --- | --- | --- |
| **组件** | `T{Feature}` | `TLoginForm`, `TDataTable`, `TAdminLayout` |
| **Store** | `use{Feature}Store` | `useAuthStore`, `useUserStore`, `useAppStore` |
| **Adapter 工厂** | `create{Framework}{Feature}Adapter` | `createNaiveMessageAdapter` |
| **Plugin 工厂** | `createTnzi{Framework}` | `createTnziNaiveUi` |
| **Composable** | `use{Framework}{Feature}` | `useNaiveTheme`, `useShadcnDialog` |
| **Resolver** | `Tnzi{Framework}Resolver` | `TnziNaiveUiResolver` |
| **组件 Props** | 内联 `defineProps<{...}>()` 或 `I{Component}Props` | `ILoginFormProps`（core 契约层导出） |
| **组件 Emits** | 内联 `defineEmits<{...}>()` 或 `I{Component}Emits` | `ILoginFormEmits`（core 契约层导出） |

### 文件命名

| 类别 | 格式 | 示例 |
| --- | --- | --- |
| **Vue 组件** | `T{Feature}.vue` (PascalCase) | `TLoginForm.vue` |
| **TypeScript 模块** | `kebab-case.ts` | `message.ts`, `data-query.ts` |
| **目录** | `kebab-case/` | `adapters/`, `components/auth/` |
| **测试文件** | `{module}.test.ts` | `message.test.ts` |
| **索引文件** | `index.ts` | — |

---

## 样式规范

### 原则

1. **UI 包不定义基础样式**，由 UI 框架自身提供
2. **T* 组件样式使用 scoped CSS 或 CSS Modules**
3. **主题通过 ThemeAdapter 管理**，不在组件中硬编码颜色
4. **shadcn 包使用 Tailwind CSS**，通过 `globals.css` 引入
5. **Vant/Naive UI 包通过主题 token 自定义**

### CSS 变量前缀

如需自定义 CSS 变量，统一使用 `--tz-` 前缀：

```css
:root {
  --tz-primary: #3b82f6;
  --tz-radius: 0.5rem;
}
```

---

## 测试规范

### 测试类型

| 类型 | 目录 | 测试内容 |
| --- | --- | --- |
| 适配器测试 | `__tests__/adapters/` | 适配器方法是否正确转译 |
| Store 测试 | `__tests__/stores/` | Store 是否正确委托 manager |
| 组件测试 | `__tests__/components/` | 渲染、事件、props |

### 适配器测试示例

```typescript
import { describe, it, expect, vi } from 'vitest';
import { createNaiveMessageAdapter } from '../src/adapters/message';

describe('createNaiveMessageAdapter', () => {
  it('should call messageApi.success', () => {
    const messageApi = {
      success: vi.fn(),
      error: vi.fn(),
      warning: vi.fn(),
      info: vi.fn(),
      loading: vi.fn(() => ({ destroy: vi.fn() })),
    };

    const adapter = createNaiveMessageAdapter(messageApi);
    adapter.success('Done');

    expect(messageApi.success).toHaveBeenCalledWith('Done');
  });

  it('loading should return close function', () => {
    const destroy = vi.fn();
    const messageApi = {
      success: vi.fn(),
      error: vi.fn(),
      warning: vi.fn(),
      info: vi.fn(),
      loading: vi.fn(() => ({ destroy })),
    };

    const adapter = createNaiveMessageAdapter(messageApi);
    const close = adapter.loading('Loading...');
    close();

    expect(destroy).toHaveBeenCalled();
  });
});
```

### 运行测试

```bash
# 运行所有包的测试
pnpm test

# 运行单个包的测试
pnpm --filter @tnzi/ui-admin test

# 类型检查
pnpm typecheck
```

---

## 新包创建清单

创建新的 UI 包时，按以下清单逐项完成：

### 1. 脚手架 (Scaffold)

- [ ] 创建 `packages/{name}/package.json`
  - `name`: `@tnzi/{name}`
  - `peerDependencies`: `vue ^3.5.0`
  - `dependencies`: `@tnzi/core`, 对应 UI 框架
- [ ] 创建 `packages/{name}/tsconfig.json`
  - 继承根 `tsconfig.json`
  - `references` 引用 `../core`
- [ ] 创建 `packages/{name}/vite.config.ts`
  - lib 模式
  - `external`: `vue`, UI 框架包
- [ ] 在根 `tsconfig.json` 中添加 `references`
- [ ] 在根 `package.json` 的 `build` 脚本中添加构建步骤

### 2. 适配器 (Adapters)

- [ ] `src/adapters/message.ts` — 实现 `MessageAdapter`
- [ ] `src/adapters/dialog.ts` — 实现 `DialogAdapter`
- [ ] `src/adapters/theme.ts` — 实现 `ThemeAdapter`
- [ ] `src/adapters/index.ts` — 统一导出
- [ ] 适配器测试

### 3. Store 包装 (Stores)

- [ ] `src/stores/auth.ts` — 包装 `AuthStateManager`
- [ ] `src/stores/user.ts` — 包装 `UserStateManager`
- [ ] `src/stores/app.ts` — 包装 `AppStateManager`
- [ ] `src/stores/index.ts` — 统一导出

### 4. 组件 (Components)

- [ ] `src/components/index.ts` — 统一导出
- [ ] 按优先级实现 T* 组件（参见下方路线图）

### 5. Plugin

- [ ] `src/plugin.ts` — `createTnzi{Framework}()` 工厂函数
- [ ] `src/index.ts` — 主入口导出

### 6. 解析器 (Resolver)

- [ ] `src/resolvers/index.ts` — `Tnzi{Framework}Resolver`

### 7. 文档

- [ ] `README.md` — 包说明文档
- [ ] 根 `README.md` 中添加包链接

---

## 组件开发路线图

### Phase 1 — 认证与布局 (Authentication & Layout)

| 组件 | 说明 | 依赖 |
| --- | --- | --- |
| `TLoginForm` | 登录表单 | `ILoginFormProps` |
| `TRegisterForm` | 注册表单 | `IRegisterFormProps` |
| `TAdminLayout` | 管理后台布局 | `RouterAdapter` |
| `TSidebar` | 侧边栏导航 | `RouterAdapter` |

### Phase 2 — 数据展示 (Data Display)

| 组件 | 说明 | 依赖 |
| --- | --- | --- |
| `TDataTable` | 数据表格 | `DataQueryController` |
| `TDataList` | 数据列表 | `DataQueryController` |
| `TPagination` | 分页控件 | `PaginationController` |
| `TEmpty` | 空状态 | — |

### Phase 3 — 表单 (Form)

| 组件 | 说明 | 依赖 |
| --- | --- | --- |
| `TForm` | 通用表单 | `FormController` |
| `TSearchForm` | 搜索表单 | `FormController` |
| `TDynamicForm` | 动态表单 | `FormController` + Schema |

### Phase 4 — 扩展 (Extension)

| 组件 | 说明 | 依赖 |
| --- | --- | --- |
| `TFileUpload` | 文件上传 | Storage API |
| `TRichEditor` | 富文本编辑器 | — |
| `TUserCard` | 用户卡片 | `AuthStateManager` |

---

## 附录: vite.config.ts 模板

```typescript
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import dts from 'vite-plugin-dts';

export default defineConfig({
  plugins: [
    vue(),
    dts({ rollupTypes: true }),
  ],
  build: {
    lib: {
      entry: {
        index: 'src/index.ts',
        resolvers: 'src/resolvers/index.ts',
      },
      formats: ['es'],
    },
    rollupOptions: {
      external: [
        'vue',
        '@tnzi/core',
        // UI 框架包
        'naive-ui',    // 或 'vant', 'shadcn-vue'
        'pinia',
      ],
      output: {
        entryFileNames: '[name].mjs',
        chunkFileNames: 'chunks/[name]-[hash].mjs',
      },
    },
  },
});
```

## 附录: package.json 模板

```json
{
  "name": "@tnzi/{name}",
  "version": "0.1.0",
  "description": "Tnzi UI components for {Framework}",
  "type": "module",
  "main": "./dist/index.mjs",
  "module": "./dist/index.mjs",
  "types": "./dist/index.d.ts",
  "exports": {
    ".": {
      "import": "./dist/index.mjs",
      "types": "./dist/index.d.ts"
    },
    "./resolvers": {
      "import": "./dist/resolvers.mjs",
      "types": "./dist/resolvers.d.ts"
    }
  },
  "files": ["dist"],
  "scripts": {
    "build": "vite build",
    "typecheck": "vue-tsc --noEmit",
    "test": "vitest run",
    "test:unit": "vitest run",
    "clean": "rimraf dist"
  },
  "peerDependencies": {
    "vue": "^3.5.0"
  },
  "dependencies": {
    "@tnzi/core": "workspace:*"
  }
}
```
