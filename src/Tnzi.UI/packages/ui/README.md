# @tnzi/ui

> The common layer of the Tnzi.NET frontends, under `@tnzi/ui-admin` and `@tnzi/ui-ai`: the theme context
> and its `--tnzi-*` CSS variables, the login stack, overlay and focus primitives, and the components that
> render without a component library. It depends on no component library (no naive-ui, no vant): the
> naive-rendered components live in `@tnzi/ui-admin`.

## 安装

```bash
pnpm add @tnzi/ui          # @tnzi/core 会被一并装上
pnpm add vue @iconify/vue  # 必需 peer
pnpm add -D unocss         # 原子类引擎
```

可选 peer（按需）：

```bash
pnpm add pinia   # 仅当使用 @tnzi/ui/stores
```

表格、表单、弹窗外壳等用 Naive UI 渲染的组件在 `@tnzi/ui-admin`（它自己声明 naive-ui）；对话型产品用 `@tnzi/ui-ai`，不需要 naive-ui。

> **不需要**任何 `tailwind.config.js` / `postcss.config.js`。

## 注册

```ts
import { createApp } from 'vue'
import { createTnziUi } from '@tnzi/ui'
import '@tnzi/ui/style.css'
import 'virtual:uno.css'
import App from './App.vue'

createApp(App).use(createTnziUi()).mount('#app')
```

插件提供主题上下文、设置 runtime 适配器（路由 + 存储）、`provideI18n`，并全局注册本包的组件。它不注册 message / dialog 一类适配器：
那些驱动组件库，由 `@tnzi/ui-admin` 的 `createTnziUiAdmin` 注册。用 `@tnzi/ui/stores` 时先 `app.use(createPinia())`，
再给插件传 `httpClient`。

## 导出子路径

| 子路径 | 内容 |
| --- | --- |
| `@tnzi/ui` | 插件工厂 `createTnziUi`、组件、headless、主题、适配器、resolver、utils 的合集入口（不含 stores） |
| `@tnzi/ui/components` | 27 个 `T*` 组件：错误页与反馈（`T403` `TEmpty` `TResult` `TConfirm` …）、显示原语（`TAvatar` `TSvgIcon` `TDescriptions` `TMetricBars` …）、`TFilterChips`、页面布局（`TAppShell` `TContainer` `TGrid` …） |
| `@tnzi/ui/headless` | 主题上下文（`useTheme` / `THEME_CONTEXT_KEY` / `createThemeContext`）、`usePalette` / `useBreakpoints`、登录栈（`useLoginContext` / `useFormRules` / `useCaptcha` …）、`useConfirm` / `useDrawer` / `useFocusTrap` / `useOverlayLayer` / `useLoadingBar` |
| `@tnzi/ui/theme` | `defaultThemeSettings` / `mergeThemeSettings`、调色板与对比度函数、`--tnzi-*` 变量生成、`applyThemeModeToDocument`、UnoCSS 的 `presetTnzi` |
| `@tnzi/ui/stores` | `useAuthStore` / `useUserStore` / `useAppStore`（`@tnzi/core` state manager 的薄包装；需要 Pinia） |
| `@tnzi/ui/adapters` | `createThemeAdapter` / `createRuntimeAdapter` |
| `@tnzi/ui/utils` | 空值占位符、头像与设备图标函数 |
| `@tnzi/ui/resolvers` | `TnziUiResolver`（`unplugin-vue-components` 自动导入本包组件） |
| `@tnzi/ui/theme/presets/*` | 主题预设 JSON（颜色与模式） |
| `@tnzi/ui/icons` | 本包（及其 @tnzi 依赖）会渲染的 Iconify 名字清单 + `bundleTnziIcons` / `selectTnziIcons`（生成；离线打包图标用） |
| `@tnzi/ui/vite` | `@tnzi/core/vite` 的转导出：`tnziSingletons()` 让单实例依赖只解析到应用自己的一份（总是 `vue`；`vue-router`、`pinia`、`naive-ui` 在应用装了时；其余经 `extra`），任何 Vite 大版本下都能通过类型检查 |
| `@tnzi/ui/style.css` | 打包样式（必需引入）；不含任何组件库的规则 |

组件命名一律 `T` 前缀，便于与应用自有组件区分。

## 浮层层栈

页面上打开的浮层按打开顺序排成一个栈，Escape 和 Tab 只给最上面那一层：在对话框上再打开的确认框按一次 Escape 只关它自己，
下面那个对话框也不会把 Tab 拉回自己里面。`useFocusTrap(rootRef, active, { onEscape })` 自动加入；只接管按键、不困住焦点的浮层用
`useOverlayLayer(active, { onEscape, onKeydown, root })`，返回的 `isTop` 是「是不是最上面一层」。按键请走这两个的回调，
不要自己再往 `document` 上挂监听。没加入层栈的模态（例如组件库自带的模态框）里的按键不会被下层接走（需要给出 `root`），
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
本包只产出设置与 CSS 变量；把它们翻译成某个组件库的覆盖值是用那个库的包的事（naive 的在 `@tnzi/ui-admin` 的 `buildNaiveThemeOverrides`）。
自己的颜色可以用 `@tnzi/ui/theme` 的 `getContrastTextColor(fill)`、`getReadableColor(color, background)` 与 `contrastRatio(a, b)` 算。

## 文档

- [@tnzi/ui 包文档](https://tnzi.cc/docs/modules/ui) — 组件清单、主题令牌、覆盖方式
- [快速开始](https://tnzi.cc/docs/getting-started) — 安装与首个组件
- [架构](https://tnzi.cc/docs/architecture) — 五包分层与依赖方向

## License

MIT
