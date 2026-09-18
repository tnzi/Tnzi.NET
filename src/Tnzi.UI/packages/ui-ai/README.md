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
> 遗留的 shadcn 类名词表（`text-muted-foreground` / `bg-accent` / `border-border`）已于
> 2026-08-03 全部改写为生态统一的 `text-tnzi-muted` / `bg-tnzi-layout` / `border-tnzi-border`。
> **不要**引入 `tailwindcss` / `postcss.config.js`，也不要重新引入 shadcn 类名。

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

**附件**：`send(text, files)` 接受 composer 交出的 `File[]`。图片内联成 base64 的 `image` 内容部分随请求发出；
其它文件只能以 Storage 的文件 id 引用（`FileContentPartDto.fileId`），所以要给 `uploadFile`
（通常是 `useStorageApi(http).upload` 解包后的样子）；没给的话非图片文件会经 `onError` **当场拒绝、整条不发**，
而不是把文字发上去、让助手回答「我没看到文档」。`maxAttachmentBytes` 是单个附件上限（默认 10 MB）。
`TChatApp` 的 `enableAttachments` 关掉时，回形针、粘贴、拖放**三条入口都失效**，不会出现发不出去的芯片。
★后端只按 `message` 文本持久化用户消息，重开会话时看不到当初的附件。

`useChat.regenerate(id)` 把会话截断到该回答之前的那条提问再发一次 —— 列表里始终只有一份提问，
不会出现两条相同的用户气泡（08 月以前它只删助手行再 `send()`，测试还把重复钉成了预期）。

`install()` 同时接管会话生命周期：`createTnziClient` 在 401 撑过刷新后清掉认证状态，`defineChatApp` 再把用户送回登录路由并把当前位置放进 redirect query（与 `@tnzi/ui-admin` 同一套行为）。传 `guard: false` 则两者都不做，由宿主自己接。

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
`showToolCalls`（默认开）在每个回答上方渲染这一轮的工具调用卡片（流式期间实时出现，重开会话也在）；
`showUsage`（默认关）在回答下方渲染一行 token 计数。两者的数据都由 `useChatThreads` 写进 `ChatMessage`。
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
| `@tnzi/ui-ai/icons` | 本包（及其 @tnzi 依赖）会渲染的 Iconify 名字清单 + `bundleTnziIcons` / `selectTnziIcons`（生成；离线打包图标用） |
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
唯一的可视验证入口是消费应用的 chat 界面（dev 端口 6174）。

## 文档

- [@tnzi/ui-ai 包文档](https://tnzi.cc/docs/modules/ui-ai) — 入口、主题令牌、扩展点
- [架构](https://tnzi.cc/docs/architecture) — 五包分层与依赖方向

## License

MIT
