# @tnzi/ui

> Base Vue 3 UI layer for Tnzi.NET frontends — Naive UI components, UnoCSS atomic styles,
> a `--tnzi-*` CSS-variable theme system, and Pinia stores that delegate to `@tnzi/core`.

## 安装

```bash
pnpm add @tnzi/ui          # naive-ui / pinia / @tnzi/core 会被一并装上
pnpm add @iconify/vue      # 必需 peer（图标渲染）
pnpm add -D unocss         # 原子类引擎
```

可选 peer（按需）：

```bash
pnpm add echarts              # 仅当使用图表组件
pnpm add vue-draggable-plus   # 仅当使用拖拽组件
```

> Tailwind 已于 2026-04 移除。**不需要**任何 `tailwind.config.js` / `postcss.config.js`。

## 注册

```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createTnziUi } from '@tnzi/ui'
import '@tnzi/ui/style.css'
import 'virtual:uno.css'
import App from './App.vue'

createApp(App).use(createPinia()).use(createTnziUi()).mount('#app')
```

## 导出子路径

| 子路径 | 内容 |
| --- | --- |
| `@tnzi/ui` | 插件工厂 `createTnziUi`、组件、stores、headless 逻辑的合集入口 |
| `@tnzi/ui/components` | 61 个 `T*` 组件（card / data / display / feedback / form / layout / list / navigation / utility） |
| `@tnzi/ui/stores` | `useAuthStore` / `useUserStore` / `useAppStore`（`@tnzi/core` state manager 的薄包装） |
| `@tnzi/ui/headless` | `useTheme` / `usePalette` / `useBreakpoints` / `useConfirm` / `useDrawer` / `useSafeMessage` / `useFocusTrap` … |
| `@tnzi/ui/adapters/*` | `createMessageAdapter` / `createDialogAdapter` / `createThemeAdapter` … |
| `@tnzi/ui/utils` | Naive UI 辅助函数 |
| `@tnzi/ui/resolvers` | `TnziUiResolver`（`unplugin-vue-components` 自动导入） |
| `@tnzi/ui/theme/presets/*` | 主题预设 JSON |
| `@tnzi/ui/style.css` | 打包样式（必需引入） |

组件命名一律 `T` 前缀，便于与应用自有组件区分。

## 主题

单一真值源是 `--tnzi-*` CSS 变量；明暗切换是文档根元素上的 class 切换，不是 token 拷贝。
详见 [组件覆盖指南](../../../../docs/frontend/component-override.md)。

## 文档

- [快速开始](../../../../docs/frontend/getting-started.md)
- [架构](../../../../docs/frontend/architecture.md)
- [组件覆盖](../../../../docs/frontend/component-override.md)
- [排错](../../../../docs/frontend/troubleshooting.md)
- 包内设计约束与踩坑：`packages/ui/CLAUDE.md`

## License

MIT
