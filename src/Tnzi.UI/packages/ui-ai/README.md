# @tnzi/ui-ai

> AI chat & agent UI for Tnzi.NET frontends — a drop-in chat product shell, streaming message
> primitives, workflow visualisation, and an embeddable widget mode.

## 安装

```bash
pnpm add @tnzi/ui-ai
pnpm add @iconify/vue
pnpm add -D unocss
```

> 样式层是 **UnoCSS**（`presetWind4` + 内联 `presetTnzi`）。Tailwind 已于 2026-04 移除，
> **不要**引入 `tailwindcss` / `postcss.config.js`。

## 最快的接法：`defineChatApp`

整个应用的装配（登录路由 + 认证守卫 + 会话恢复）一次搞定，对话屏仍然是你自己的组件：

```ts
// main.ts
import { defineChatApp } from '@tnzi/ui-ai/plugin'

const { routes, install } = defineChatApp({
  runtime: createTnziClient({ baseUrl: '/api' }),   // @tnzi/core
  home: () => import('./pages/ChatPage.vue'),
  login: { brandName: 'Acme', subheading: 'Start creating with Acme' },
})
const router = createRouter({ history: createWebHistory(), routes })
const app = createApp(App)
app.use(router)
install(app, router)
app.mount('#app')
```

对话循环（列表 / 打开 / 发送 SSE / 中止 / 乐观侧栏条目 / 消息 id 调和）用
`useChatThreads`，接一个后端就完事：

```ts
const chat = useChatThreads({ http, chatApi, threadApi, onError: (m) => message.error(m) })
```

## 组件层接法：`TChatApp`

`@tnzi/ui-ai/chat` 导出一个已经组合好侧栏 + 落地页 + 会话流 + 输入框 + 设置弹窗 + 命令面板的组件。
应用只需接数据、听事件：

```vue
<script setup lang="ts">
import { TChatApp } from '@tnzi/ui-ai/chat'
import '@tnzi/ui-ai/style.css'
</script>

<template>
  <TChatApp
    :threads="threads"
    :messages="messages"
    :is-streaming="isStreaming"
    v-model:input-text="inputText"
    @send="onSend"
    @new-chat="onNewChat"
    @select-thread="onSelectThread"
  />
</template>
```

视觉通过插槽覆盖（`#brand`、`#topbar-actions`、`#sidebar-content`、`#composer-left`、`#settings-{id}` …）。
只有当 `TChatApp` 装不下你的设计时，才降到 `@tnzi/ui-ai/components` 自己拼（区域骨架在 `components/layout` 与 `components/overlay`）。

## 导出子路径

| 子路径 | 内容 |
| --- | --- |
| `@tnzi/ui-ai` | 合集入口 |
| `@tnzi/ui-ai/plugin` | `defineChatApp`：登录路由 + 认证守卫 + 会话恢复（需 `vue-router`，可选 peer） |
| `@tnzi/ui-ai/auth` | 登录页 `TAuthPage` / 登录路由 `TAuthRoute`（登录**逻辑**在 `@tnzi/ui`） |
| `@tnzi/ui-ai/chat` | `TChatApp` 及会话流原语 |
| `@tnzi/ui-ai/components` | 消息 / 工具调用 / 推理 / 附件 / 浮层等组件 |
| `@tnzi/ui-ai/headless` | `useChatThreads`（整套对话循环）/ `useGlobalAiTheme` / `useChat` / `useStreamMarkdown` … |
| `@tnzi/ui-ai/adapters` | 后端 DTO → 视图模型（`toChatMessage` / `toThreadItem` / `toMessageRole`） |
| `@tnzi/ui-ai/workflow` | 工作流 DAG 可视化（`@vue-flow/core`，懒加载） |
| `@tnzi/ui-ai/embed` | 嵌入式小挂件模式 |
| `@tnzi/ui-ai/theme`、`/theme/*` | 运行时主题覆盖层 |
| `@tnzi/ui-ai/i18n` | 翻译引擎（`createAiI18n` / `useAiI18n` / `formatAiMessage`），与词典分开 |
| `@tnzi/ui-ai/locales`、`/locales/*` | 语言包（按需动态导入） |
| `@tnzi/ui-ai/utils` | 格式化与 markdown 归一化 |
| `@tnzi/ui-ai/style.css` | 打包样式（必需引入） |

## 主题

`src/styles/index.css` 是调色板的唯一真值源：`:root` 声明浅色 `--tnzi-ai-*`，
`.dark` / `[data-theme="dark"]` 声明深色。明暗切换就是根元素上的 class 切换
（`TChatApp` 的 `autoApplyTheme` 默认开启会自动做）。

## ⚠️ 组件改动没有自动化浏览器覆盖

本包的 SFC 不参与单测覆盖率（需要真实 DOM + 用户交互）。原先承担这部分的 playground 与
Playwright 规格**已于 2026-08-01 删除**，因此**单测全绿不代表 SFC 改动是对的**。
唯一的可视验证入口是 Acme chat 应用（`projects/acme/src/Acme.UI/chat`，dev 端口 6174）。

## 文档

- [架构](../../../../docs/frontend/architecture.md)
- [排错](../../../../docs/frontend/troubleshooting.md)
- 包内设计约束与踩坑：`packages/ui-ai/CLAUDE.md`

## License

MIT
