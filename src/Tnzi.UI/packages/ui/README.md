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
| `@tnzi/ui/components` | 60 个 `T*` 组件（card / data / display / feedback / form / layout / list / navigation / overlay / utility） |
| `@tnzi/ui/stores` | `useAuthStore` / `useUserStore` / `useAppStore`（`@tnzi/core` state manager 的薄包装） |
| `@tnzi/ui/headless` | `useTheme` / `usePalette` / `useBreakpoints` / `useConfirm` / `useDrawer` / `useSafeMessage` / `useFocusTrap` / `useOverlayLayer` … |
| `@tnzi/ui/adapters/*` | `createMessageAdapter` / `createDialogAdapter` / `createThemeAdapter` … |
| `@tnzi/ui/utils` | Naive UI 辅助函数 |
| `@tnzi/ui/resolvers` | `TnziUiResolver`（`unplugin-vue-components` 自动导入） |
| `@tnzi/ui/theme/presets/*` | 主题预设 JSON |
| `@tnzi/ui/icons` | 本包（及其 @tnzi 依赖）会渲染的 Iconify 名字清单 + `bundleTnziIcons` / `selectTnziIcons`（生成；离线打包图标用） |
| `@tnzi/ui/style.css` | 打包样式（必需引入） |

组件命名一律 `T` 前缀，便于与应用自有组件区分。

## 浮层层栈

页面上打开的浮层按打开顺序排成一个栈，Escape 和 Tab 只给最上面那一层：在对话框上再打开的确认框按一次 Escape 只关它自己，
下面那个对话框也不会把 Tab 拉回自己里面。`useFocusTrap(rootRef, active, { onEscape })` 自动加入；只接管按键、不困住焦点的浮层用
`useOverlayLayer(active, { onEscape, onKeydown, root })`，返回的 `isTop` 是「是不是最上面一层」。按键请走这两个的回调，
不要自己再往 `document` 上挂监听。没加入层栈的模态（例如 naive 的 `NModal`）里的按键不会被下层接走（需要给出 `root`），
控件已经 `preventDefault` 的 Escape 也不会。栈本身（只管先后、不碰 DOM）是 `@tnzi/core/headless` 的 `useOverlayLayerStack()`，
本包在其上负责按键与焦点；页面上有两份本包时，它们共用同一个栈，一次按键也只交给最上面那一层一次。

## 主题

单一真值源是 `--tnzi-*` CSS 变量；明暗切换是文档根元素上的 class 切换，不是 token 拷贝。

主色可以是任何颜色（运行时 `setColor('primary', …)` 换成浅黄也一样），所以主色的两种用法不要直接写 `var(--tnzi-primary)`：

| 用法 | 变量 | 取值 |
| --- | --- | --- |
| 主色填充**上面**的文字和图标 | `--tnzi-on-primary` | 白色；白色在这个主色上低于 3:1 时换成近黑墨色 `#141414` |
| 主色**当作**文字或图标的颜色 | `--tnzi-primary-text` | 主色本身；在当前明暗的背景上读不到 4.5:1 时，同色相加深（浅色）或提亮（深色）到刚好够 |

主题上下文每次换主色都会重算它们，`--tnzi-primary-text` 由 `.dark` 类在 `--tnzi-primary-text-on-light` 与 `-on-dark` 之间切换。
浅色主色时，naive 画在主色填充上的文字（主色按钮、勾选框、可勾选标签、日历今天、日期选择的选中项、步骤条、浮动按钮）也换成同一种墨色。
自己的颜色可以用 `@tnzi/ui/theme` 的 `getContrastTextColor(fill)`、`getReadableColor(color, background)` 与 `contrastRatio(a, b)` 算。

## 文档

- [@tnzi/ui 包文档](https://tnzi.cc/docs/modules/ui) — 组件清单、主题令牌、覆盖方式
- [快速开始](https://tnzi.cc/docs/getting-started) — 安装与首个组件
- [架构](https://tnzi.cc/docs/architecture) — 五包分层与依赖方向

## License

MIT
