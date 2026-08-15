# @tnzi/mobile

> Tnzi UI components based on Vant 4 for Vue 3 + Vite mobile SPA applications.

## 安装

```bash
pnpm add @tnzi/mobile
```

## 使用

### Vue 3 + Vite 配置

```typescript
// main.ts
import { createApp } from "vue";
import TnziMobile from "@tnzi/mobile";
import "@tnzi/mobile/style.css";
import App from "./App.vue";

createApp(App)
    .use(TnziMobile, {
        locale: "zh-CN",
    })
    .mount("#app");
```

### 当前能力

```vue
<template>
    <div>Vite SPA 插件已安装，可使用 Vant 组件并接入 core adapters</div>
</template>
```

- 提供 Vue 插件（安装 Vant + 默认样式 + core 集成适配器）
- 对外导出 `T*` 业务组件与常用 Vant 组件别名（`VButton`、`VCard` 等）
- 提供移动端视口 Hook（`useMobileViewport`）
- 按 `@tnzi/core/components` 契约实现移动端 `T*` 组件
- 已实现导航语义组件：`TMenu`、`TNavBar`、`TTabBar`

## 本地开发

```bash
# 库 watch 构建：改 src 自动出 dist，消费方的 Vite 会热更
pnpm -C packages/mobile dev
```

本包目前没有对应的消费应用（现有消费方只有 admin / chat / site 三个 Web 端），
改动靠 `pnpm -C packages/mobile test` 与消费方项目验证。

## 内置依赖

- `vant` ^4.9.0
- `vue` ^3.5.0

## License

MIT
