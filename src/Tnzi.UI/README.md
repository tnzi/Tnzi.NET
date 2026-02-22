# Tnzi UI Ecosystem

> Enterprise-grade UI component library for Tnzi.NET frontend applications.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![pnpm](https://img.shields.io/badge/maintained%20with-pnpm-cc00ff.svg)](https://pnpm.io/)

## 项目定位

Tnzi.UI 是 Tnzi.NET 框架的**前端组件生态系统**，将后端框架的模块化理念延伸到前端。

- **不是** .NET 程序集，而是一个 **pnpm monorepo** 前端项目（`Tnzi.UI.csproj` 仅为空壳，用于挂入解决方案和 CI/CD 集成）
- 采用 **核心层 + 适配层** 分离架构：业务逻辑集中在 `@tnzi/core`（与 UI 无关），三个 UI 包只是不同场景的视图皮肤
- 通过 **Contract Sync** 机制（`Tnzi.Cli` + OpenAPI/Swagger）自动生成前端 API 契约，确保前后端类型同步
- **核心价值**：写一次业务逻辑，换 UI 库只需换包（shadcn → vant → naive-ui）

## Packages

| 包名 | 基础框架 | UI 库 | 适用场景 |
| --- | --- | --- | --- |
| [@tnzi/core](./packages/core/README.md) | TypeScript 5.9+ / @vue/reactivity | - | 所有项目（核心基础） |
| [@tnzi/shadcn](./packages/shadcn/README.md) | Vite 5+ + Vue 3.5+ | shadcn-vue | 面向外部的站点、现代 Web 应用 |
| [@tnzi/vant](./packages/vant/README.md) | Vite 5+ + Vue 3.5+ | Vant 4.9+ | 移动端 APP / PWA |
| [@tnzi/naive-ui](./packages/naive-ui/README.md) | Vite 5+ + Vue 3.5+ | Naive UI 2.40+ | 内部管理系统、企业后台 |

## 架构设计

```
@tnzi/core (Vue-reactive 核心)
├── services/     — 9 个业务模块契约 (types + schemas + api)
├── state/        — 响应式状态管理器 (Auth/User/App)
├── headless/     — 无头交互控制器 (Pagination/Selection/Sort/Form/DataQuery)
├── adapters/     — 适配器接口 (message/dialog/theme/router/storage/i18n)
├── components/   — 组件接口标准 (I*Props/I*Emits)
├── http/         — HTTP 客户端 + 中间件
├── schemas/      — Zod 验证模式
└── types/enums/constants/errors/utils

@tnzi/{shadcn|vant|naive-ui} (轻量实现层)
├── adapters/     — 1. 实现 core 适配器接口 (message → toast, dialog → modal, theme → CSS)
├── stores/       — 2. Pinia store 封装 (10~20 行/store，委托 core/state)
├── components/   — 3. T* 组件 (纯视图，用 core/headless + UI 库原子组件)
└── plugin.ts     — 4. Vue 插件 (一键安装)
```

**核心原则**: 业务逻辑在 core，UI 包只做视图适配。

### @tnzi/core 详解

core 是整个生态的灵魂，不依赖任何 UI 框架：

| 模块 | 说明 |
| --- | --- |
| `services/` | 9 个业务模块 API 契约（identity, app, audit, chat, notification, storage, template, payment, ai），每个包含 `types.ts` + `schemas.ts` + `api.ts` + `metadata.ts` |
| `services/*/generated/` | 由 `Tnzi.Cli` 从 OpenAPI/Swagger 自动生成的代码，**不要手动修改** |
| `state/` | 响应式状态管理器（Auth/User/App），基于 `@vue/reactivity`，无需 Vue 组件即可使用 |
| `headless/` | 无头交互控制器（Pagination/Selection/Sort/Form/DataQuery），纯逻辑无 UI |
| `adapters/` | 适配器接口定义（message/dialog/theme/router/storage/i18n/icons），由 UI 包实现 |
| `http/` | HTTP 客户端 + 中间件管道 |
| `components/` | 组件接口标准（`I*Props`/`I*Emits`），定义统一的组件 API 契约 |
| `schemas/` | Zod 验证模式 |
| `types/enums/constants/errors/utils` | 基础类型定义和工具函数 |

### UI 包职责（shadcn / vant / naive-ui）

三个 UI 包结构几乎一致，各自只做 4 件事：

1. **`adapters/`** — 实现 core 的适配器接口（如 message → toast/notify，dialog → modal）
2. **`stores/`** — Pinia store 薄封装（10~20 行/store），委托给 core/state
3. **`components/`** — `T*` 前缀的业务组件，用 core/headless + 对应 UI 库的原子组件拼装
4. **`plugin.ts`** — Vue 插件，一键安装

### 前后端契约同步

通过 `pnpm contracts:sync` 调用 `Tnzi.Cli`，从后端 OpenAPI JSON 自动生成前端的 DTO 类型、Zod Schema、API 调用函数，输出到各 service 模块的 `generated/` 目录：

```bash
# 从运行中的后端同步
pnpm contracts:sync:url

# 从本地 openapi.json 文件同步
pnpm contracts:sync

# 仅检查是否有变更（CI 用）
pnpm contracts:sync:check
```

## 特性

- **统一接口** — 所有 UI 包实现相同的组件接口 (`I*Props`, `I*Emits`)
- **响应式核心** — core 基于 `@vue/reactivity`，状态管理器和控制器开箱即用
- **业务复用** — 通过 `@tnzi/core/state` 共享认证、分页等逻辑，UI 包无需重写
- **依赖内置** — UI 包内置第三方库，用户开箱即用
- **类型安全** — 完整的 TypeScript + Zod 运行时验证

## 组件前缀规范

**所有 Tnzi 组件统一使用 `T` 前缀** (Tnzi)

```vue
<template>
    <!-- Tnzi 组件 - T 前缀 -->
    <TLoginForm />
    <TDataTable />
    <TUserCard />

    <!-- 原生 UI 库组件 - 保持原前缀 -->
    <Button />          <!-- shadcn-vue -->
    <van-button />      <!-- Vant -->
    <n-button />        <!-- Naive UI -->
</template>
```

**不要在同一个项目中混用多个 @tnzi UI 包！**（T* 组件名称会冲突）

## 快速开始

### 面向外部站点 → @tnzi/shadcn

```bash
pnpm add @tnzi/shadcn
```

```typescript
import { createApp } from "vue";
import TnziUi from "@tnzi/shadcn";

const app = createApp(App);
app.use(TnziUi);
app.mount("#app");
```

### 移动端 → @tnzi/vant

```bash
pnpm add @tnzi/vant
```

```typescript
import { createApp } from "vue";
import TnziMobile from "@tnzi/vant";
import "@tnzi/vant/style.css";

const app = createApp(App);
app.use(TnziMobile);
app.mount("#app");
```

### 内部管理系统 → @tnzi/naive-ui

```bash
pnpm add @tnzi/naive-ui
```

```typescript
import { createApp } from "vue";
import TnziNaiveUi from "@tnzi/naive-ui";

const app = createApp(App);
app.use(TnziNaiveUi);
app.mount("#app");
```

### 仅使用 core（自行选择 UI 库）

```bash
pnpm add @tnzi/core
```

```typescript
import { AuthStateManager, DataQueryController } from "@tnzi/core";

// 响应式状态管理器，直接可用
const auth = new AuthStateManager(deps);
await auth.login(credentials);

// 无头数据查询控制器
const query = new DataQueryController({ fetchFn: api.getList });
await query.fetch();
```

## 文档

- [核心包文档](./packages/core/README.md)
- [shadcn 包文档](./packages/shadcn/README.md)
- [vant 包文档](./packages/vant/README.md)
- [naive-ui 包文档](./packages/naive-ui/README.md)
- [使用指南](./USAGE.md)
- [UI 包开发规范](./UI-PACKAGE-GUIDE.md)
- [npm 发布指南](./PUBLISHING.md)

## 开发

```bash
# 安装依赖
pnpm install

# 构建所有包
pnpm build

# 类型检查
pnpm typecheck

# Service Contract Sync (OpenAPI)
pnpm contracts:sync:url
pnpm contracts:sync
pnpm contracts:sync:check

# 运行 playground
pnpm -r dev
```

## 依赖管理

### 内置依赖

| 封装包 | 内置依赖 | 版本 |
| --- | --- | --- |
| `@tnzi/core` | `@vue/reactivity`, `zod` | `^3.5.0`, `^3.24.0` |
| `@tnzi/shadcn` | `shadcn-vue`, `pinia` | `^0.9.0`, `^2.2.0` |
| `@tnzi/vant` | `vant` | `^4.9.0` |
| `@tnzi/naive-ui` | `naive-ui`, `pinia` | `^2.40.0`, `^2.2.0` |

## 版本历史

- **v0.1.0** (2026-02-17)
    - 初始版本
    - core 基于 `@vue/reactivity`，包含 `state/` 响应式状态管理器 + `headless/` 无头交互控制器
    - 三个 UI 包：`@tnzi/shadcn`（Web）、`@tnzi/vant`（移动端）、`@tnzi/naive-ui`（管理后台）
    - 适配器体系：message/dialog/theme/router/storage/i18n/icons
    - 基于 Zod 的全模块 Schema 覆盖
    - Contract Sync 机制（Tnzi.Cli + OpenAPI）

## 许可证

[MIT](LICENSE) (c) Tnzi.NET

## 相关链接

- [shadcn-vue](https://www.shadcn-vue.com/)
- [Vant 4](https://vant-ui.github.io/vant/)
- [Naive UI](https://www.naiveui.com/)
- [Vue 3](https://vuejs.org/)
- [Tnzi.NET](https://github.com/tnzi/tnzi.net)
