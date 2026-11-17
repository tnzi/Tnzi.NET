# @tnzi/ui-ai

> AI chat & agent UI for Tnzi.NET frontends — a drop-in chat product shell, streaming message
> primitives, workflow visualisation, and an embeddable widget mode.

## 安装

```bash
pnpm add @tnzi/ui-ai @tnzi/ui @tnzi/core
pnpm add @iconify/vue
pnpm add -D unocss
```

必需的 peer 是 `vue`、`@iconify/vue`、`@tnzi/core`、`@tnzi/ui`；`vue-router` 只有 `@tnzi/ui-ai/plugin`（`defineChatApp`）要。
只用控件的页面（桌面窗口、小工具页）装 `@tnzi/ui-ai vue @iconify/vue` 即可，导入 `@tnzi/ui-ai/controls` 与 `@tnzi/ui-ai/controls.css`，见「控件」一节的「只用控件」。
**本包不依赖 naive-ui**：按钮、字段、下拉、对话框、弹层、提示都是本包自己画的（`@tnzi/ui-ai/controls` 与 `components/overlay`），
不用在外面包 `NConfigProvider`。`@tnzi/ui` 也不依赖任何组件库，所以**只用本包的应用不装 naive-ui**（`pinia` 也不需要）。
门禁 `__tests__/conventions/main-path-no-naive.test.ts` 沿导入图检查每一个入口（`import type` 也算），要求本包只从 `@tnzi/ui` 的子路径
（`@tnzi/ui/headless`、`@tnzi/ui/theme`、`@tnzi/ui/components`）导入、不经根出口，并检查构建出的 `dist/style.css` 里没有 `.n-*` / `--n-*` 规则。

> 样式层是 **UnoCSS**（`presetWind4` + 内联 `presetTnzi`）。Tailwind 已于 2026-04 移除，
> 遗留的 shadcn 类名词表（`text-muted-foreground` / `bg-accent` / `border-border`）已于
> 2026-08-03 全部改写为生态统一的 `text-tnzi-muted` / `bg-tnzi-layout` / `border-tnzi-border`。
> **不要**引入 `tailwindcss` / `postcss.config.js`，也不要重新引入 shadcn 类名。
> 构建里唯一的 PostCSS 处理是 `vite.config.ts` 内联的悬停插件（见「触屏」）。

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

`login` 里的品牌（`brandName` / `brandIcon` / `brandWordmark`）同时用在登录页与访客欢迎页（`guestWelcome`）。这些值在建路由时定下来，
要随界面语言换品牌名或字标的，自己挂 `TAuthRoute` / `TGuestWelcome`（`login.component` / `guestWelcome.component`）并绑定响应式的值。

对话循环（列表 / 打开 / 发送 SSE / 中止 / 乐观侧栏条目 / 消息 id 调和）用
`useChatThreads`，接一个后端就完事：

```ts
const chat = useChatThreads({ http, chatApi, threadApi, onError: (m) => message.error(m) })
```

**附件**：`send(text, files)` 接受 composer 交出的 `File[]`。图片与 PDF 内联成 base64 的 `image` / `document` 内容部分随请求发出；
其它文件只能以 Storage 的文件 id 引用（`FileContentPartDto.fileId`），所以要给 `uploadFile`
（通常是 `useStorageApi(http).upload` 解包后的样子）；没给的话非图片文件会经 `onError` **当场拒绝、整条不发**，
而不是把文字发上去、让助手回答「我没看到文档」。★框架默认的后端解析器**不加载文件内容**，带 `file` 内容部分的请求返回 501
（`AI_UNSUPPORTED_MEDIA_TYPE`），所以只有后端替换了 `IAgentResolver`、能按 id 读文件时才给 `uploadFile`；
对接默认后端时不给，图片与 PDF 以外的文件在上传之前就被拒绝。`maxAttachmentBytes` 是单个附件上限（默认 10 MB）。
对话的是以外部 CLI 运行的 agent、后端这一轮收文件（`ICliFileAttachmentPolicy`，见后端文档 ai-cli §13.3）时给 `inlineFiles: true`：
图片与 PDF 以外的文件（`.eml`、`.csv`、`.docx`、`.zip`……）也内联成 `document` 内容部分发出，带文件名与浏览器给的类型（没有时 `application/octet-stream`），
agent 用自己的工具打开它；它优先于 `uploadFile`。这类文件的单个上限是 `maxFileAttachmentBytes`（缺省同 `maxAttachmentBytes`，与后端的
`AI:Chat:Attachments:MaxFileBytes` 对齐）。后端不收文件的一轮答 415，经 `onError` 报告、整条不发。
`TChatApp` 的 `enableAttachments` 关掉时，回形针、粘贴、拖放**三条入口都失效**，不会出现发不出去的芯片。
**粘贴**：在资源管理器 / 访达里复制文件、在输入框里按 Ctrl+V / Cmd+V 就把它们附上（Chromium 与 WebView2 把复制的文件放在 `clipboardData.files`），截图照旧；
每个文件过同一套检查（`composerAccept`、大小，被拒的经 `attachments-rejected` 报告），同一张截图不会因为剪贴板里列了两遍而附上两次。
剪贴板里的文字只是这些文件的名字或路径时不再粘进输入框；别的文字（例如表格区域连同它的图片一起复制）照常粘贴，只有文字的粘贴不受影响。
绑定了外部 CLI 运行时的 agent 的对话里，附件随用户消息存档，重开会话时照样显示（内容经 `resolveAttachmentUrl` 读，见下文）；
内建引擎的对话只按 `message` 文本存用户消息，重开会话时看不到当初的附件。

**打开会话**：`selectThread(id)` 取回会话内容之后，才把 `activeThreadId` 与 `messages` 一起切过去，所以屏幕上的会话始终就是
发送的去处。加载失败时停在原会话（经 `onError` 报告，那一行可以再点）；加载期间发出的消息留在原会话，并取消这次切换。
加载期间 `openingThreadId` 指着正在打开的那个会话：交给 `TChatApp` 的 `openingThreadId`，点下去那一行当场高亮（图标换成转圈），
主区换成这个会话的加载态（`TThreadLoading`，顶栏写它的标题；会话不在 `threads` 里、而在产品自己的 `navGroups` 里时，
由 `threadTitleOf(threadId)` 告诉外壳它叫什么），发送等内容到了再放开；不接的话，内容到达之前屏幕上什么都不变，
慢网络下读起来像点了没反应。加载期间再点屏幕上的那个会话（当前会话，或新会话第一轮的乐观行）表示「留在这里」，取消那次打开；
再点正在打开的那个不会再取一次，返回同一个 promise。

**新会话的 id**：新会话第一轮发出时侧栏先有一行乐观行，`activeThreadId` 还是空的；流里第一个带 `threadId` 的事件
（通常是 `run_started`）一到，乐观行换成服务端的会话，`activeThreadId` 随即变成这个 id，流照常继续，不重读消息。
所以第一轮还在运行时，按会话 id 做的事（地址栏改成会话的路由、`queueWhileRunning` 把消息排进这个会话）就已经能做；
`done` 再带一次同一个 id 时什么都不变。在服务端说出 id 之前离开的新会话不会把人拉回去。
打开时只取最近的 `messageLimit` 条（默认 100），更早的经 `loadOlderMessages()` 往回翻。
侧栏列表按 `pageSize`（默认 50）分页：`loadMoreThreads()` 取下一页，`searchThreads(keyword)` 按标题搜（空串回到全部）。

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
`showAgentByline`（默认开）是每条回答上方那一行（agent 的标记、`agentName`、`agentLabel`）；关掉后回答直接从正文开始，
还在写的回答在第一个字出来之前、以及工具调用之后接着干活时显示一个跳动的点，有字之后由正文末尾的光标接手。

**品牌**：侧栏头部是品牌标记（`brandLogo`，默认内置的 T 字标，其它值当 Iconify 名字）加品牌名（`brandName`）。
产品有画好的字标（logotype）时给 `brandWordmark`（Iconify 名字，先 `addIcon` 注册），头部把品牌名的文字换成它：
高度是品牌名字号的 1em，宽度按图标自己的宽高比，在文字原来占的那一行里垂直居中，行高保持不变，所以旁边的标记、右端的操作和下面的导航都不动；
图标里的 `currentColor` 跟着明暗主题走，其它固定颜色（比如 i 上那个黄点）保持原样。字母要看起来与正文一样大，就在 viewBox 里留出上下空白。
`brandName` 仍以视觉隐藏的文字留在原处，读屏软件读的是它，所以两个都要给；设置里的品牌行照旧用 `brandName` 的文字。

**顶栏标题**：顶栏写当前会话的标题，或 `view` 所在导航项的名字。没有可写的（新对话 / 落地页、还没有标题的会话、导航里没有对应项的视图）时顶栏标题留空，
栏高与右侧的按钮不变；侧栏展开、收起成图标栏、完全隐藏（含手机上关着的抽屉）时都一样。品牌属于侧栏头部，
放在顶栏里读起来像一个并不存在的页面标题。
`#topbar-left` / `#topbar-title` 插槽拿到的 `title` 同理，可能是空串。
访客欢迎页（`TGuestWelcome`）与登录页（`TAuthPage` / `TAuthRoute`）接受同名的 `brandWordmark`，效果相同。

**品牌标记就是收起按钮**：平时显示标志，键盘聚焦时、以及鼠标这类能悬停的指针悬停时（`@media (hover: hover)`），同一个方框里换成侧栏图标，点它把侧栏收成图标栏（窄屏抽屉里是关闭）。
触屏上标志始终是标志（轻触后浏览器会把 `:hover` 留在原处，而打开抽屉的顶栏按钮正好在这个位置），轻触它照样关闭抽屉。
按钮的可访问名称是动作（「Collapse sidebar」/「Close sidebar」），里面的标志只是装饰。收起后的图标栏同理：标志在悬停或聚焦时变成展开按钮。
系统开了「减少动态效果」时切换不做过渡。

**头部右端的产品控件**：`#brand-actions` 插槽放在品牌名之后、贴着头部右端（比如一个只有图标的模式切换），插槽属性 `{ narrow }`。
图标栏里不显示（56px 放不下；要在那里放东西用 `#rail` 插槽）。整个替换头部的 `#brand` 插槽拿到 `{ toggleSidebar, toggleLabel, narrow }`，
自己画的头部用它们就不会丢掉收起功能：

```vue
<TChatApp brand-name="Acme">
  <template #brand-actions>
    <ModeSwitch />   <!-- 产品自己的控件，例如 role="radiogroup" 的两个图标单选 -->
  </template>
</TChatApp>
```

```vue
<TChatApp brand-logo="brand:mark" brand-name="Acme" brand-wordmark="brand:wordmark" ... />
```

**agent 的标记**：`agentIcon` 是署名里那个小方块里的图标（Iconify 名字，默认 `lucide:sparkles`，产品自己的图标先 `addIcon` / `addCollection` 注册），
`agentStreamingIcon` 是回答还在写时换上的那一个（比如带 SMIL 动画的同一个形象）。`#agent-mark` 插槽整个替换那个小方块，
拿到 `{ message, streaming, placement }`。关掉署名时，给了流式标记（`#agent-mark` 插槽，或 `agentStreamingIcon`）就用它代替跳动的点，
高度正好一行正文，插槽这时拿到 `placement: 'pending'`；只给 `agentIcon` 不算（那是静止的标记，表达不了「正在写」），仍是跳动的点。
三者都不给时，署名与跳动的点和原来一模一样。

```vue
<TChatApp agent-icon="brand:face" agent-streaming-icon="brand:face-glancing" ... />
```

回答下方的操作行（复制、重新生成、赞、踩）平时隐藏，悬停在这一轮上或其中有键盘焦点时出现，隐藏时也占着位置，所以出现时什么都不挪；
没有悬停能力的设备（触屏）上始终显示。
`showToolCalls`（默认开）渲染这一轮的工具调用卡片（流式期间实时出现，重开会话也在）：外部 CLI agent 的每次调用
带着它发生时回答写到了第几个字符（`textOffset`），卡片就落在正文里的那个位置，「先看日志 [卡片] 日志显示 [卡片] 于是改了」
读起来是一段叙述而不是两张卡片加一段回头指代的文字；不带位置的调用（内建 agent）仍在回答上方。经典气泡布局（`TChatMessage`）
画的是同一种卡片、同一套按工具名的渲染器，只是放在气泡上方；
`showUsage`（默认关）在回答下方渲染一行 token 计数。两者的数据都由 `useChatThreads` 写进 `ChatMessage`。
绑定了外部 CLI 运行时的 agent 会把同一次工具调用发两次（开始与结果，同一个 `id`，带 `status`）：
`useChatThreads` 按 `id` 把它们合成一张卡片，卡片以 `status` 为准显示运行中 / 完成 / 失败。自己处理流的消费方照此按 `id` 合并。
只有当 `TChatApp` 装不下你的设计时，才降到 `@tnzi/ui-ai/components` 自己拼（区域骨架在 `components/layout` 与 `components/overlay`）。

**从外面把文件放进输入框**：拿 `TChatApp` 的组件引用调 `addAttachments(files)`。桌面宿主的截图按钮、操作系统交过来的文件
（「发送到」、拖到托盘图标上）用的就是它：文件与拖进来的一样过 `composerAccept`（类型）与 `composerMaxFileSize`（大小）两道检查，
被拒的同样发 `attachments-rejected`，收下的停在输入框里等用户写字，**不会自己发出去**。

```vue
<TChatApp ref="chat" enable-attachments @attachments-rejected="(list) => notify(list)" ... />
<script setup lang="ts">
const chat = useTemplateRef<InstanceType<typeof TChatApp>>('chat')
desktopHost.onFilesHandedOver((files) => chat.value?.addAttachments(files))
</script>
```

返回 `{ added, rejected, pending }`；附件关着（`enableAttachments` 为假）时返回 `null`。主区正显示 `#view` 页面时没有输入框：
文件先存着（`pending`），外壳请求回到对话（`update:view` 发出 `'chat'`，`v-model:view` 时自动切过去），输入框一出现就落进去。
`focusComposer()` 把焦点放回输入框（光标在末尾）。单独用 `TThreadComposer` / `TLandingPage` 时，它们的组件引用上也有同名的 `addAttachments`。

**运行中排队**：`queue-while-running`（默认关）让用户在一轮运行时写下一条消息。输入框里有文字或附件时，主按钮变成排队按钮、停止按钮挪到它左边；
点排队或按回车发出 `queue(content, files)`，参数与 `send` 相同（附件是原样的 `File[]`，照普通发送的方式上传），输入框随即清空。
输入框为空时主按钮仍是停止。存放队列、轮到时发出、在 `#thread-composer-top` 里列出排队的消息（改、删、立即发送）都归消费方。
关着时运行中只有停止按钮，回车不发送。

```vue
<TChatApp queue-while-running :is-streaming="chat.isStreaming.value" @queue="(text, files) => queue.add(text, files)" ... >
  <template #thread-composer-top><QueuedMessages :items="queue.items" /></template>
</TChatApp>
```

**输入框上的建议菜单**（斜杠命令、@提及，任何「打一个触发字符、从列表里挑」的菜单）：`TChatApp` 的 `#composer-suggestions` 插槽渲染在输入框正上方、与它等宽，
拿到 `ComposerSuggestionContext`：`text`、`selectionStart` / `selectionEnd`、`token`（光标前那一段不含空白的字符，`{ text, start, end }`，
刚打完空格或选中了一段时为空）、`replaceRange(start, end, insert)`（光标落在插入内容之后、焦点留在输入框）、`setText(text, caret?)`、`focus()`。
输入框的每个按键先以 `composer-keydown` 发出、再由输入框处理：菜单开着时在这里接管方向键、Enter、Tab、Escape 并 `preventDefault()`，
输入框就不处理这个键，Enter 因此用来选而不是发送。`composerInputAttrs` 落在 textarea 上，给 combobox 的 ARIA（`role`、`aria-expanded`、
`aria-controls`、`aria-activedescendant`）。什么时候算「开着」、挑中之后写什么，都由消费方决定：

```vue
<TChatApp
  v-model:input-text="text"
  :composer-input-attrs="{ role: 'combobox', 'aria-expanded': String(open), 'aria-controls': 'slash-menu' }"
  @composer-keydown="onKey"
>
  <template #composer-suggestions="{ token, replaceRange }">
    <SlashMenu
      v-if="token.text.startsWith('/') && token.start === 0"
      id="slash-menu"
      :query="token.text.slice(1)"
      @pick="(name) => replaceRange(token.start, token.end, `/${name} `)"
    />
  </template>
</TChatApp>
```

`onKey` 在菜单开着时处理导航键并调 `event.preventDefault()`。单独用 `TThreadComposer` 时是 `#suggestions` 插槽、`input-keydown` 事件与 `inputAttrs`，
组件引用上另有 `getSelection()`、`replaceRange()`、`setText()` 与 `inputElement`（菜单渲染在别处时用）。`tokenBeforeCaret` / `replaceTextRange` 是同一套计算的纯函数。

**斜杠命令菜单**：给 `TChatApp` 一份 `slashCommands`（外部 agent 的技能与命令，`SlashMenuItem`：`command` 是斜杠后面 CLI 认的写法，
`name` 是显示的短名，可选 `description`、`argumentHint`、`sourceLabel`、`icon`），输入框里只有一个斜杠加正在打的名字时（CLI 只认消息开头的命令），
输入框上方就出现菜单，按名字筛选：名字或命令以它开头的排最前，其次是命令里某一段（`team-shop:release` 的 `shop`）以它开头的，再次是包含它的，最后是描述里有它的。
↑↓ 移动（到头回绕），Enter 或 Tab 把 `/<command> ` 写进框里（后面留一个空格写参数），Esc 关掉菜单直到文字再变；没有匹配时 Enter 照常发送；
输入法确认候选的那次 Enter 不归菜单。点一项就选它，按下鼠标不会让框失去焦点。框上带 combobox 的 ARIA（`aria-expanded`、`aria-controls`、
`aria-activedescendant` 指着高亮的那一项），所以读屏软件跟得上。菜单与 `#composer-suggestions` 共用位置，菜单开着时代替插槽内容。
命令从哪里来归应用：`slash-query` 在开始打命令时给出斜杠后面的名字（不打命令时是 `null`），在那时去读这个会话能用的命令；
读的期间 `slashCommandsState: 'loading'`，读失败 `'failed'`，列表为空时菜单据此说「正在读取」「读不到」「没有匹配」；`slashCommandsNote` 是列表下面的一行说明。
不给 `slashCommands`（或给 `null`）就没有菜单，框也不带 combobox 角色。

```vue
<TChatApp
  v-model:input-text="text"
  :slash-commands="commands"
  :slash-commands-state="commandsState"
  @slash-query="(q) => q !== null && loadCommands()"
/>
```

自己的输入框用 `TSlashCommandMenu`（`items`、`highlighted`、`id`、`state`、`note`，事件 `pick(index)`、`highlight(index)`）与 `useSlashMenu`
（`@tnzi/ui-ai/headless`：`open`、`query`、`results`、`highlighted`、`pick`、`dismiss`、`handleKey(event)`），纯函数 `slashQuery`、`filterSlashItems`、`textWithCommand`。

**上下文用量**：`TContextMeter` 显示对话的上下文窗口在上一轮结束时用了多少：一个小圆环加占用比例（「12%」）；不知道窗口大小时显示 token 数和一个仪表图标。
悬停或键盘聚焦时提示「上下文 24K / 200K」，这句也是它的无障碍名称（`role="img"`，可以 Tab 到）。超过 80% 用警示色，超过 95% 用危险色（下一轮可能要压缩历史）。
数字来自外部 CLI 运行的 `CliRunDto.contextTokens` / `contextWindow`（`contextUsageOf(run)` 换成 `{ tokens, window }`，CLI 没报时为 `null`），
在一轮结束时读一次这一轮的运行（`useCliRunApi(http).get(runId)`）。尺寸按输入框工具条做，放在 `#thread-composer-right` 里：

```vue
<TChatApp ...>
  <template #thread-composer-right>
    <TContextMeter v-if="context" :tokens="context.tokens" :window="context.window" />
  </template>
</TChatApp>
```

纯函数在 `@tnzi/ui-ai/headless`：`contextShare`、`contextLevel`、`sharePercent`、`compactTokens`、`contextUsageOf`，阈值 `CONTEXT_WARN_SHARE`（0.8）、`CONTEXT_FULL_SHARE`（0.95）。

**图片查看器**：消息里的图片（`TMessageAttachments` 的缩略图、工具卡片里的大图，都是 `TAttachmentImage`）点一下（或聚焦后 Enter / Space）在页面上方打开 `TImageViewer`：
深色底上的原图，按原比例尽量大。同一条消息的附件图、同一次工具调用返回的图是一组：查看器打开在点的那一张上，←→ 键或两侧按钮在组内前后切换（到头回绕），
底部一条小工具栏：缩小、当前倍数（按一下回到适应屏幕）、放大，以及「2 / 5」；只有一张时没有切换按钮和计数。
图片打开时适应屏幕，最多放大 8 倍：工具栏按钮、`+` / `-` 键（`0` 回到适应屏幕）、滚轮或触控板捏合（以指针处为中心）、双指捏合、
双击（放大到 2.5 倍或回到适应屏幕）；放大后鼠标或手指拖动图片平移，←→↑↓ 键平移而不再切换图片，图片边缘不会被拖进画面里；
切到另一张时回到适应屏幕。它是模态对话框（`role="dialog"`、`aria-modal`，名称是当前图片的文件名）：打开时焦点进入、Tab 只在它的按钮间循环，
Esc、关闭按钮、点图片外的深色区域、手机上的返回手势关闭，焦点回到原来的缩略图；打开期间页面不滚动。读不到的图片在组里照样占一格，显示「这张图片没能加载」。
查看器的代码在第一次点击时才加载。自己的工具渲染器（比如一组截图）要让图片成组时，在外层组件里调 `provideImageGroup()`，里面的 `TAttachmentImage` 按挂载顺序加入；
也可以直接用 `TImageViewer`（`images`: `{ src, alt }[]`，`v-model:index`，`null` 为关闭）；只用控件的页面从 `@tnzi/ui-ai/dialogs` 导入，样式在 `controls.css` 里（见「只用控件」）。
它与 `TDialog` 共用对话框的层叠深度，从对话框里打开时画在对话框之上，Esc 先关查看器。深色底是令牌 `--tnzi-ai-image-viewer-backdrop`（默认 90% 黑，明暗模式相同，页面只隐约可见），按钮与文字是 `--tnzi-ai-on-image`；按钮的圆底是在这层深色底上掺 12% 的 `--tnzi-ai-on-image`，工具栏的底就是它，所以图片放大后从按钮下面经过时按钮仍是深色、图标仍看得清。改深色底时按钮与工具栏一起跟着变。
深色底铺满整个窗口，图片、关闭按钮与左右按钮让开页面的顶部留白（`--tnzi-overlay-inset-top`，无边框桌面窗口自己的标题栏按钮），与对话框、提示条一样；不设这个变量时位置不变。

**输入框正上方的内容**（排队的消息、产品自己的提示）：`TChatApp` 的 `#thread-composer-top` 插槽渲染在对话输入框正上方，
与输入框等宽、相隔 8px，占据布局，和输入框一起留在 sticky 区域里，所以读者往上翻对话时它也一直看得见。插槽属性 `narrow` 表示当前是不是窄屏。
多张卡片之间相隔 8px。插槽里没有渲染出任何元素（里面的 `v-if` 为假）时不占位、不留间距，所以卡片可以按状态出现和消失。
内容超过窗口高度的 40% 时这一块自己滚动，长计划不会把对话挤到够不着的地方。
它出现、变高或消失时，跟随到底的对话照常跟随；「滚到最新」按钮浮在它上方，被它挡住的最后一条消息也算「在下面」。
只在对话里显示，落地页（还没有对话）没有这个位置。单独用 `TThreadComposer` 时是 `#top` 插槽；
浮在框上方、不占位的 `#above`（「滚到最新」按钮用的）在两者都有时浮在 `#top` 之上。

```vue
<TChatApp ...>
  <template #thread-composer-top>
    <QueuedMessages v-if="queue.items.length" :items="queue.items" />
  </template>
</TChatApp>
```

agent 等用户回答的卡片（批准、提问、计划）也在这个位置，排在插槽内容之后，由外壳自己画，见下文「输入框上方的卡片」。

**对话末尾的内容**（会话已结束之类的状态标记）：`TChatApp` 的 `#thread-end` 插槽渲染在最后一条消息之后、输入框之前，
和消息同在会滚动的线程列里，随对话一起滚走，不像 `#thread-composer-top` 那样留在 sticky 区域。插槽属性 `narrow` 表示当前是不是窄屏。
正在打开对话（加载骨架）时与落地页上都不渲染；不给这个插槽时线程里没有它的包装元素。
它是对话的一部分：出现或变高时，跟随到底的对话照常跟随；「滚到最新」按钮以它的末端为对话的末端，它被输入框挡住时按钮就出现。
插槽里没有渲染出任何元素时不算，仍以最后一条消息为准。放 `.t-wave-divider` 时与消息之间的间距和「后台任务后继续」那条分隔线一样。

```vue
<TChatApp ...>
  <template #thread-end>
    <div v-if="session.ended" class="t-wave-divider" role="status"><span>已合并，会话已结束</span></div>
  </template>
</TChatApp>
```

**工具栏的排版**：文字与工具栏之间 10px（窄屏 `compact` 更紧），工具栏的图标按钮是 16px 图标。输入框变窄时左侧那组先让：
`#left` 里的选择器自己截断标签（只剩图标也行），右侧那组（模型选择、主按钮）和每个图标按钮保持原尺寸。

**单行输入框**：`TThreadComposer` 的 `inline` 把左侧一组、文字、右侧一组排成一行，外框是 22px 圆角的胶囊，适合放在卡片或页头里的输入框
（概览页的问候卡片）。文字变长时照样向下长，两组按钮停在最后一行；附件与拒收提示（以及 `#notice` 的内容）各占一整行，在这一行上面；
窄屏下文字 16px（iOS 不再缩放页面）。与 `sticky`、`compact` 可以同时用。

```vue
<TThreadComposer v-model="text" inline :sticky="false" @send="onSend">
  <template #left><AgentPicker /></template>
</TThreadComposer>
```

**主按钮**：输入框工具栏最右端只有一个主按钮，做什么取决于输入框里的状态（判定是纯函数 `composerPrimaryAction`，同样导出）：

| 状态 | 按钮 | 可访问名称（en） |
|---|---|---|
| 一轮正在进行（`TThreadComposer` 的 `running`；`TChatApp` 里是 `isStreaming`） | 停止，发 `stop` | `chat.stop` |
| 正在语音输入 | 结束听写，已听到的文字留在框里；按钮填充强调色并脉动，占位文字与读屏状态是 `composer.listening` | `composer.stopRecording` |
| 有文字，或至少一个附件（只有附件也算） | 发送 | `composer.send` |
| 空，且这里能用语音输入 | 麦克风 | `composer.voiceInput` |
| 空，且不能用语音输入 | 发送，禁用 | `composer.send` |

一轮进行中时，框里有文字也是停止：文字留在框里，Enter 不发送，也不排队，这一轮结束后再发。
语音输入中按 Enter 照常发送，同时结束听写，之后迟到的识别结果不会再填回已清空的框；一轮开始（或 `enableVoice` 关掉）时听写也随之结束。
按钮在所有状态下是同一个元素、同一个位置和尺寸，所以切换状态不会挪动工具栏里的其它控件（`#right` 里的模型选择器之类）。
它有 `data-action`（`stop` / `end-dictation` / `send` / `voice`）以及 `is-stop`、`is-recording`、`is-voice`、`is-ready` 四个状态类。
`#send` 插槽可以整个替换它，插槽属性是 `action`（判定结果 `{ kind, enabled }`）、`canSend`、`send`、`stop`、`toggleVoice`。
发送状态的图标可以换成产品自己的（品牌标记之类）：`sendIcon` 是一个 Iconify 名字，`TChatApp` 上给一次，首页与会话里的输入框都用它；
`TThreadComposer` / `TLandingPage` 单独用时是同名 prop，访客欢迎页是 `defineChatApp({ guestWelcome: { sendIcon } })`（或 `TGuestWelcome` 的 `sendIcon`）。
只换发送这一个状态：停止、麦克风、结束听写的图标不变；不给时是 `lucide:arrow-up`（导出为 `DEFAULT_SEND_ICON`）。
打包图标的应用（`bundleTnziIcons`）要自己注册这个名字（`addIcon` / `addCollection`），它不在 `tnziIconNames` 里。

```vue
<TChatApp send-icon="mdi:paw" ... />
```
`compact` 时工具栏按钮是 40px 的触摸目标。

**语音输入**：主按钮在空框时是麦克风，用浏览器的 Web Speech 识别（`enableVoice`，默认开；`voiceLang` 是识别语言，改了下次开始就生效）。
浏览器没有识别器、页面不是安全上下文（https 或 localhost）、或者权限策略关掉了麦克风（`Permissions-Policy: microphone=()`、iframe 没给 `allow="microphone"`）时不出现麦克风，空框时主按钮是禁用的发送。
第一次点击时浏览器会问要不要给麦克风。识别停下的每一种原因都有一句话，说发生了什么、能怎么办（词典里的 `composer.voice*`）：
麦克风被这个网站禁止了（去地址栏左边的站点设置里允许）、没获得权限、浏览器不在这里提供识别服务、没有可用的麦克风、连不上识别服务、没听到声音、不支持这种语言。
有识别器却没有识别服务的宿主（嵌入式 webview）在第一次使用时报「连不上识别服务」或「没获得权限」；确定用不了的宿主里把 `enableVoice` 关掉。
自己做输入框时用 `useVoiceInput`，`error` 是 `VoiceInputErrorCode`，文案自己定。

**账号菜单的子菜单**：`userMenuExtraItems`（或 `TUserMenu` 的 `items` / `extraItems`）里带 `children` 的项打开一层子菜单。
用鼠标（`(hover: hover) and (pointer: fine)` 且窗口不窄于 768px）时，指针停在该项上即在菜单旁边飞出子菜单，点击也会打开，
原菜单保持不动；同一时间只开一个，指针斜穿其他行进入子菜单不会关掉它，离开菜单和子菜单片刻后才关闭。再下一层在飞出面板里原地展开。
触屏（包括混合设备上用手指点）和窄屏上，子菜单在原地展开、顶上一行返回，因为手机上没有地方飞出。
键盘：Enter / Space / → 打开并把焦点移进去，↑ ↓ Home End 在里面移动，← / Escape 回到打开它的那一项，整个菜单不关。
`checked` 标出当前选中的那一项，所以语言、主题这类选择器可以直接放进账号菜单。选中子项时 `user-menu` 发的是子项的 `id`，整棵树里的 id 要唯一（`lang:en`、`theme:dark`）。

```ts
const extraItems = computed(() => [
  { id: 'language', label: t('language'), value: currentLanguageName.value, children: [
    { id: 'lang:en', label: 'English', checked: locale.value === 'en' },
    { id: 'lang:fr', label: 'Français', checked: locale.value === 'fr' },
  ] },
])
```

**账号行**：侧栏底部的账号按钮（头像 + 名字，点开账号菜单）按内容定宽，不铺满整行；悬停（只在能悬停的指针上）与菜单打开时铺一层
与其它侧栏行相同的 `--tnzi-ai-hover` 底色，键盘聚焦时另有一圈焦点框。行高跟着行尺寸令牌走（桌面 32px，窄屏 44px）。
按钮右侧、贴着行的右端可以放图标按钮（应用中心、通知、反馈）：`userBarActions` 是 `{ id, icon, label }` 的数组，
`label` 同时是提示文字与可访问名称，点击发 `user-action`（参数是 `id`），不会顺带打开账号菜单。
收起成图标栏时这些按钮不显示（那里只有头像，要放东西用 `#rail` 插槽）；窄屏抽屉里照常显示，尺寸是触屏尺寸。

```vue
<TChatApp
  :user-bar-actions="[{ id: 'apps', icon: 'lucide:layout-grid', label: 'Get apps and extensions' }]"
  @user-action="(id) => id === 'apps' && router.push('/apps')"
/>
```

**侧栏分组标题**：最近会话的标题（「Recents」）与 `navGroups` 里带标题的分组用同一套样式（`styles/index.css` 的 `.t-sidebar-heading`）：
字号字重与行相同（行尺寸令牌，桌面 14px / 400），只靠颜色区分，标题用 `--tnzi-ai-sidebar-heading`（浅色 3.5:1、暗色 4.9:1），
行用正文色 `--tnzi-ai-text`；标题框 28px 比 32px 的行矮，上方留 `--tnzi-ai-sidebar-section-gap`（12px，行间距是 1px），
标题顶到第一行顶正好一个行距。文字与行的文字（导航行是图标）左对齐，没有图标列。可折叠的标题是按钮（`aria-expanded`），
折叠箭头在悬停或键盘聚焦时出现、折叠后常驻。两个令牌都能经 `applyAiTheme({ sidebarHeading })` / `applyThemeVars` 覆盖。

**侧栏行的悬停与选中**：侧栏里的每一行和每个按钮（导航行、新建对话、图标栏、会话行与它们的操作按钮、账号按钮、底部图标、品牌收起按钮）
共用两个中性填色：悬停 `--tnzi-ai-sidebar-hover`（浅色 `rgba(0,0,0,.05)`，暗色 `rgba(255,255,255,.075)`），
选中 `--tnzi-ai-sidebar-selected`（浅色 `rgba(0,0,0,.10)`，暗色 `rgba(255,255,255,.15)`）。选中行**不用品牌色**：背景、文字、图标都不染色，
字重也不变（黄色或绿色的品牌色淡染出来像荧光笔划过）；读屏软件经 `aria-current` 知道是哪一项。
最近会话里打开着的那一条用悬停色作选中底色，导航入口用选中色（图标栏里的最近对话面板例外，见下文）。两个令牌可经 `applyAiTheme({ sidebarHover, sidebarSelected })` 覆盖。

**长标题悬停滚动**：最近会话与侧栏导航的行，标题被截断时，鼠标停在行上约 300ms 后标题以 30px/s 匀速向左滚到末尾并停住
（不循环、不来回），两端用 12px 的渐隐代替省略号；移开立刻回到开头、省略号回来。触屏不触发；系统开了「减少动态效果」时直接跳到末尾；
行的「...」菜单开着时保持展开状态。溢出不超过 8px 的标题不动。自己画的行可以用 `@tnzi/ui-ai/headless` 的同一套：
标题元素上写 `v-title-reveal`（值为「保持」，例如菜单是否打开），悬停区域（整行）加 `data-title-reveal-host` 属性；
不用 Vue 时调 `createTitleReveal(title, host)`，返回 `{ setHold, destroy }`。标题元素自己要是 `overflow: hidden` + `white-space: nowrap` + `text-overflow: ellipsis`，
渐隐样式在 `@tnzi/ui-ai/style.css` 里。

**导航图标的悬停动效**：侧栏导航（展开态与图标栏）的每一项可以给图标挑一个一次性动效，`NavItem.iconMotion`：

| 值 | 动作 | 适合 |
|---|---|---|
| `ring` | 抬起、绕顶部中心摇两下、落回 | 闹钟、铃铛、定时任务 |
| `hop` | 向右上弹跳一下、回落 | 启动器、插件、agent |
| `tilt` | 绕底部中心摆一下 | 笔、魔杖、灯泡 |
| `spin` | 转一整圈、略冲过头再回正 | 齿轮、刷新 |
| `pop` | 原地先缩后弹 | 书、库、其它 |

指针每进入一次行就播放一次（400-600ms，结束在静止姿态上），停在行上不会反复播放，键盘聚焦到行时也播放一次。
触屏不播放，系统开了「减少动态效果」时不播放。动效只变换整个图标（Iconify 图标是单个 SVG，内部的部件动不了），不影响布局。
不设 `iconMotion` 的项图标保持静止：没有默认动效，因为同一个动作放在每个图标上只是噪音，动效要说出这一项是什么。
外壳自己的入口带着各自的动效：新建对话 `tilt`（`newChatIconMotion` 可改，`null` 关掉），内置视图 agents `hop`、scheduled `ring`、
artifacts `pop`、skills `tilt`、knowledge `pop`。

```ts
const mainNav: NavItem[] = [
  { id: 'scheduled', label: 'Scheduled', icon: 'lucide:clock', iconMotion: 'ring' },
  { id: 'apps', label: 'Apps', icon: 'lucide:blocks', iconMotion: 'hop' },
]
```

自己画的行用 `@tnzi/ui-ai/headless` 的 `vIconMotion`：写在行上，图标上加 `data-icon-motion="ring"`；不用 Vue 时调 `createIconMotion(row)`。
关键帧在 `@tnzi/ui-ai/style.css` 里。

**导航行的快捷操作**：侧栏导航（`mainNav` 与 `navGroups`）的一行可以在右端带一个图标按钮，`NavItem.action`（`NavItemAction`：`{ id, icon, label }`），
例如「Projects」行上的「+」直接新建项目而不先打开项目页。按钮在指针停在行上或行内有键盘焦点时出现，触屏（无悬停）上常驻；
`label` 既是按钮的可访问名称，也是它的提示（与图标栏同一套 `vTooltip`）。行上的 `badge` 与按钮占同一个位置，按钮出现时 badge 让开。
点按钮时 `TChatApp` 发 `nav-item-action`（`(itemId, actionId)`，与分组标题按钮的 `nav-group-action` 同一形状），**不**发 `nav`、不改 `view`、不选中这一行；
直接用 `TSidebarNav` 时事件是 `item-action`。行本身的选中、选中态、图标动效、长标题滚动和 badge 照旧。图标栏里没有这个按钮。
带操作的行是一个盒子里并排的两个按钮（行按钮与操作按钮，按钮不嵌套），盒子是悬停区域；不带操作的行与原来完全一样。

**导航角标的档位**：`NavItem.badgeTone`（`NavBadgeTone`：`'danger' | 'warning'`）决定 `badge` 有多醒目。不给是原来的灰色小字（只是一个数）；
`'warning'` 是琥珀底、近黑字的胶囊（有东西在等，不急），`'danger'` 是红底白字的胶囊（要人现在处理）。两种底色在明暗两套主题里相同，
字色按底色选（白字配红 4.8:1、近黑字配琥珀 8.9:1，11px 也达标），令牌是 `--tnzi-ai-badge-danger` / `--tnzi-ai-on-badge-danger` /
`--tnzi-ai-badge-warning` / `--tnzi-ai-on-badge-warning`。收起侧栏的图标栏（`TChatRail`）把带档位的角标画在图标右上角，并接在按钮名称后读出
（「Inbox, 3」）；不带档位的灰色计数图标栏照旧不画。自己往图标栏里放 `TRailButton` 的，传 `badge` 与 `badgeTone` 即同样显示。

```ts
const groups = [{ id: 'main', items: [
  { id: 'inbox', label: 'Inbox', icon: 'lucide:inbox', badge: urgent, badgeTone: urgent > 0 ? 'danger' : undefined },
  { id: 'tasks', label: 'Tasks', icon: 'lucide:list-todo', badge: waiting, badgeTone: 'warning' },
] }]
```
按钮尺寸取行尺寸令牌 `--tnzi-ai-row-action-size`（桌面 24px，窄屏 36px）。

```ts
const mainNav: NavItem[] = [
  { id: 'projects', label: 'Projects', icon: 'lucide:folder',
    action: { id: 'create', icon: 'lucide:plus', label: 'Create project' } },
]
// <TChatApp :main-nav="mainNav" @nav-item-action="(itemId, actionId) => ..." />
```

**图标栏的提示（tooltip）**：侧栏收成 56px 的图标栏后没有文字，图标栏里的每个控件（品牌 / 展开按钮、每个导航入口、设置、账号头像）
都在旁边显示一个带样式的提示：指针停留约 350ms 后出现（不是浏览器 `title` 那一秒多的延迟），键盘聚焦时立即出现，
从一个图标直接移到相邻图标时不再等待；触屏上长按出现（见「控件」一节的提示气泡），按下、移开、失焦、滚动、Escape 都会让它消失。提示画在 `<body>` 上、`position: fixed`、
位于 `--tnzi-ai-z-tooltip` 层，所以不会被 `overflow: hidden` 的侧栏裁掉，默认在控件右边，放不下时翻到另一边。
它只是装饰（`aria-hidden`），控件的可访问名称不变：导航入口是各自的 `label`，品牌按钮是「Expand sidebar」，
头像是「Account menu」，头像的提示文字是账号名（`TUserMenu` 的 `tooltip` 属性可改成别的字符串，`false` 关掉；账号菜单打开时不显示）。
颜色是 `--tnzi-ai-tooltip-bg` / `--tnzi-ai-tooltip-fg`（默认取正文色与表面色，所以亮色下是深底、暗色下是浅底）。

自己的图标栏按钮用同一套：

```vue
<script setup lang="ts">
import { TRailButton, vTooltip } from '@tnzi/ui-ai/chat'
</script>

<template>
  <!-- 图标栏风格的按钮（放在 TChatApp 的 #rail 插槽、或自己的图标栏里）：label 同时是可访问名称和提示 -->
  <TRailButton icon="lucide:folder" label="Projects" :active="view === 'projects'" @click="open('projects')" />

  <!-- 任何只有图标的控件 -->
  <button v-tooltip="'Search'" aria-label="Search"><Icon icon="lucide:search" /></button>
  <button v-tooltip="{ text: 'Filter', side: 'bottom' }" aria-label="Filter">...</button>
</template>
```

`TRailButton` 的属性：`label`（必填）、`icon`、`active`、`iconMotion`、`tooltip`（`false` 不显示，字符串替换文字）、`tooltipSide`（默认 `right`）；
其余属性（`aria-current`、`data-*`、`class`）落到 `<button>` 上。`v-tooltip` 的值是文字，或 `{ text, side, delay, disabled }`；
控件要有自己的可访问名称，完全没有名称（没有 `aria-label` / `aria-labelledby`、也没有文字）的控件才会拿提示文字作 `aria-label`。
不用 Vue 时调 `@tnzi/ui-ai/headless` 的 `createTooltip(el, value)`，返回 `{ update, show, hide, shown, destroy }`。

**图标栏里的最近对话**：图标栏在导航入口下面有一个对话按钮（`railThreadsIcon`，默认 `lucide:messages-square`），
指针停在上面（约 150ms）、键盘聚焦或点击时，在图标栏旁边打开最近对话的面板：数据就是展开侧栏用的那一份（`threads`、`activeThreadId`），
只有标题，打开着的那一条用选中色标出（`aria-current`；面板里指针总停在某一行上，所以这里不用悬停色），太长时面板内滚动，
有 `recents` 时末尾有一行「View all」进入 Recents 页。列表为空时显示「No conversations yet.」而不是一个空框。

- 指针可以从按钮移进面板：面板在按钮所在的同一个元素里，两者之间的空隙由面板透明的内边距盖住；离开两者 300ms 后关闭，期间移回来不会关。
- 点击打开的面板固定住：移开不关，再点一次按钮、在外面按下、Escape 或选中一条才关。
- Escape 关闭并把焦点还给按钮；在按钮上按 ↓ / → 进入面板，面板里 ↑ / ↓ 在各行间移动，Tab 按文档顺序也能进去。
- 选中一条发 `select-thread` 并关闭面板，与展开侧栏点一条完全相同（窄屏抽屉、视图切换都走同一条路）。

`threads` 为空、也没有给 `#rail-threads` 插槽时没有这个按钮；`railThreads: false` 总是不显示。按钮的名称和面板标题是
`railThreadsLabel`，没给时取 `threadsLabel`，再没有取语言包的标题（「All tasks」）。窄屏抽屉永远是完整侧栏、不是图标栏，所以那里没有这个按钮；
展开的侧栏不变。

列表不是 `threads` 的产品（比如列的是自己的会话）用 `#rail-threads` 插槽提供面板内容，按钮照样出现。插槽属性
`{ close, select, threads, activeThreadId, openingThreadId }`：`select(id)` 关闭面板并发 `select-thread`，`close()` 只关闭。
`TRailThreadList` 可以直接拿来画自己的列表（`threads` 是 `{ id, title, origin? }[]`，另有 `activeThreadId`、`showViewAll`、`emptyLabel`、`viewAllLabel`、
`threadIcon`，事件 `select` / `view-all`）：

```vue
<TChatApp :threads="[]" rail-threads-label="Sessions" @select-thread="openSession">
  <template #rail-threads="{ select, close }">
    <TRailThreadList
      :threads="sessions.map((s) => ({ id: s.id, title: s.name }))"
      :active-thread-id="currentSessionId"
      empty-label="No sessions yet."
      show-view-all
      view-all-label="All sessions"
      @select="select"
      @view-all="() => { close(); router.push('/sessions') }"
    />
  </template>
</TChatApp>
```

图标栏之外要同样的「按钮 + 旁边的面板」，用 `TRailFlyout`（属性 `label`、`icon`、`iconMotion`、`active`、`width` 默认 280、
`openDelay`、`closeDelay`、`showHeading`；默认插槽拿到 `{ close }`；`@update:open` 报告开关）。
何时开何时关的规则在 `@tnzi/ui-ai/headless` 的 `createFlyoutController`，定位用的是同一处的 `placeBeside`。

## 对话功能：`useChatThreads` 接到 `TChatApp`

```vue
<script setup lang="ts">
const chat = useChatThreads({ http, chatApi, threadApi, onError: (m) => message.error(m) })
void chat.loadThreads()
</script>

<template>
  <TChatApp
    :threads="chat.threads.value"
    :active-thread-id="chat.activeThreadId.value"
    :opening-thread-id="chat.openingThreadId.value"
    :messages="chat.messages.value"
    :is-streaming="chat.isStreaming.value"
    v-model:input-text="chat.inputText.value"
    :threads-has-more="chat.hasMoreThreads.value"
    thread-searchable
    :has-older-messages="chat.hasOlderMessages.value"
    :loading-older-messages="chat.loadingOlderMessages.value"
    :resolve-attachment-url="chat.resolveAttachmentUrl"
    enable-edit
    @send="(text, files) => chat.send(text, files)"
    @stop="chat.abort()"
    @regenerate="() => chat.regenerate()"
    @edit="(id, content) => chat.editMessage(id, content)"
    @new-chat="chat.newChat()"
    @select-thread="chat.selectThread"
    @delete-thread="chat.deleteThread"
    @load-more-threads="chat.loadMoreThreads()"
    @search-threads="chat.searchThreads"
    @load-older-messages="chat.loadOlderMessages()"
    :recents="chat.recents"
  />
</template>
```

**最近对话（`recents`）**：传 `chat.recents` 之后，侧栏的会话列表就是一个可用的列表，而不只是一列标题：

- 标题行点一下折叠 / 展开（折叠状态和侧栏模式记在同一个 `sidebarStorageKey` 下）；悬停或键盘聚焦时出现「查看全部」「搜索」「筛选与分组」。
- 筛选与分组菜单：状态（活跃 / 已归档 / 全部）、最近发言（1 / 3 / 7 / 30 天 / 全部）、分组（日期 / 不分组）、排序（名称 / 创建时间 / 最近发言）。
  状态、时间窗与排序随每一页发给服务端，翻页始终正确；分组只排列已经加载的行。四项选择按浏览器记住
  （`useChatThreads({ recentsStorageKey })`，默认 `'tnzi-ui-ai-recents'`，`null` 不记）。
- **顺序**（服务端与 `compareThreads` 同一条规则）：先是**待你处理**的对话（`awaitingOwner`：一轮停在权限请求、问题或应用自己的确认卡片上），
  再是**进行中**的（`running`），然后其余的；每一层里按你最近一次发消息的时间（`userMessageAt`，没有时用创建时间）新的在前，同值按创建时间新的在前。
  「排序」选的是层内顺序：「最近发言」（默认）、「名称」、「创建时间」；任何排序下前两层都在最前。只有你自己的动作挪动一行：发消息、编辑、重新生成、
  排队一条消息时那一行立刻挪上去；AI 回答、一轮结束、改标题、打开或读一条对话都不挪它，除非它因此进出前两层。未读不是一层，只是行上的点。
  服务端在数据库里分层，所以待你处理与进行中的对话总在第一页。
- 「按日期分组」时，待你处理与进行中的行在日期标题之上，各有自己的标题（「Needs you」「Working」，词典 `recents.groupAwaitingOwner` / `recents.groupRunning`）；
  日期分组（今天、昨天、过去 7 天……）只用于其余的行，按最近发言的时间分（按创建时间排序时按创建时间分）。
- 一个应用里有多个 Agent 且各管各的列表时，`useChatThreads({ listAgentIds: () => [agentId] })` 把侧栏列表、搜索和完整最近对话页都限定到这些 Agent（服务端先过滤再分页）；不传则不限。
- 每行的「…」菜单：重命名（就地编辑）、归档 / 取消归档、删除（删除仍是行内确认，仍走 `delete-thread`）。
- 「查看全部」把 `view` 设为 `'recents'`（导出常量 `RECENTS_VIEW_ID`，经 `update:view`）。有 `recents` 时外壳自己渲染完整的
  最近对话页（`TRecentsPage`：活跃 / 已归档、搜索、最近发言时间、翻页、同样的行操作，顺序同侧栏），`#recents` 插槽可以换掉它；
  自己按 URL 路由视图的产品把这个 id 映射到自己的地址即可。
- 自己先过滤掉一部分会话再交 `threads` 的产品，用 `recents-include` 把同一个判断也交给完整页面。
- 每条会话行尾的来源图标见下文「对话来源图标」（`thread-icon`）。
- **顶栏标题**：打开一个已保存的会话时，顶栏的标题点一下就地变成输入框（有边框与聚焦光圈；宽度跟着文字走：打开时与原标题一样宽，输入时随之变宽变窄，
  最少约几个字的宽度，最多到顶栏剩余的空间为止，再长就在框内滚动，不会挤到顶栏右侧的按钮；
  整句选中；回车或离开保存，Esc 取消，空的不保存），
  标题后的小箭头打开菜单：重命名、归档 / 取消归档、删除（在顶栏里就地确认，删除仍走 `delete-thread`）。重命名走 `recents.rename`，
  侧栏、最近对话页与顶栏同步更新。新对话的落地页、还在创建或正在打开的会话上标题仍是普通文字。组件单独导出为 `TThreadTitle`。
  标题（以及普通文字的标题）最宽 `min(520px, 55%)`，再长以省略号截断：再长也读得出是标题，顶栏剩下的地方留空。
  框架没有「置顶」与「分享会话」的数据，这两项不出现。
- 完整的最近对话页：搜索在「活跃 / 已归档」那一行里就地展开（最宽 384px，x 或 Esc 收起并清空），每个会话一行，标题在左、最近发言时间（没有时为创建时间）在右。
- 不传 `recents` 时列表与此前一样：没有筛选菜单、没有查看全部，行菜单里只有删除，顶栏标题不可编辑。

归档要服务端的 `AgentThread.ArchivedTime` 列，升级后消费方要生成一次迁移（见升级说明）。

- **停止**：`abort()` 就是停止按钮，只在用户要停的时候调用。它停下本地的流；这一轮是外部 CLI 运行时（流的第一个事件交出了运行 id，`activeRunId`），
  同时请服务端取消它。只断开连接不会让服务端停下。运行 id 还没到时按下停止，这一轮在界面上立刻停下，请求留着等它的第一个信号：
  交出运行 id 就取消那次运行再断开，别的信号（内置 agent，回答就在这个请求上）就断开，服务端随之停下。停止不会因为来得太早而丢掉。
- **离开不是停止**：打开另一个会话、新建对话（`selectThread` / `newChat`，删除当前会话也走 `newChat`）或离开页面，只关掉屏幕上的流，
  什么都不取消，回答那一行也不标成已停止。运行接着跑，回到那个会话时接上它，回答接着长，停止按钮照样能用；正在恢复断线的一轮同样不取消，恢复就此结束。
  运行 id 还没到的一轮，请求留着：交出运行 id 时断开（运行自己接着跑），内置 agent 的一轮留到服务端答完为止（它的内容不上屏，
  答完后侧栏重读；人已经回到那个会话、又没有别的回答在进行时重读那个会话）。新会话的第一轮在运行中被离开时，侧栏那一行换成服务端给的会话 id，留着可以点回去。
- **刷新之后接上**：打开一个还有运行在跑的会话时，`useChatThreads` 找到那次运行并从头接上它的流，回答接着长出来，停止按钮照样能用。
- **跟上在别处开始的一轮**：`refreshThread()` 重读当前会话，有运行在跑就像打开会话时一样接上它（服务端发出了排队的消息、另一个标签页发了一条；排队的消息由 `messageQueue` 自己调它）。
  新的一页到了才替换 `messages`，不清屏；正在等后台任务的一轮先放手（不取消）。没有已保存的当前会话、正在打开会话或本页正有一轮在输出时什么都不做。
- **一次运行几条回答**：外部 CLI 起了后台任务时，它先回答，任务结束后自己再回答一次（服务端在两条之间发 `turn_completed`）。
  `useChatThreads` 在那里把当前这条回答定稿（换成服务端存下的消息 id），在它后面开一条新的回答；运行在最后一条之后才结束。
  自己用 `streamChat` 的消费方按同样的方式处理这个事件。
  **等后台任务的那段时间**（agent 起的开发服务器之类，最长 `AI:Cli:BackgroundTaskWait`）**对话不算忙**：`isStreaming` 为 false、`activeRunId` 清空，
  输入框照常可用。新开的那一行 `status` 为 `'waiting'`（不是流式的，`isStreaming` 为 false，还没有内容），
  `backgroundTasks` 是服务端在 `turn_completed` 里报的任务（`id` / `description` / `kind`，可能为空）；`TThreadMessage` 在行根上加
  `t-thread-message--waiting`，安静地显示「后台仍在运行」和任务说明（`.t-thread-message__waiting`、`.t-thread-message__waiting-tasks`），没有生成标记与操作按钮。
  这条流保持打开：任务结束、CLI 自己再回答时，第一个信号（文字、思考、工具调用）一到，这一行变回 `'streaming'`、`backgroundTasks` 清空，
  这一轮又算忙（停止按钮出现），回答照常流进来。等待期间发送（以及重新生成、编辑）先放掉这条流、去掉等待的那一行，**不取消运行**：
  服务端把还在跑的进程连同后台任务交给新的这一轮。等待期间调用 `abort()` 会取消那次运行，后台任务随之结束，等待的那一行去掉。
  刷新或回到会话后从头接上运行时，已经存在线程里的回答不会再出现一份，屏幕与刷新前一样：前面的回答，然后是等待的那一行。
  任务结束却没有再回答、或等到了上限，这一行在运行结束时去掉（服务端没有为它存消息，刷新后也没有它）。
  **两条回答之间画一条带文字的波浪线**（`TChatApp`）：一条回答紧接在另一条回答后面、中间没有用户消息时，说明 agent 在后台任务结束后
  自己接着说了。外壳在第二条前面画 `.t-wave-divider`（`.t-chat-app__continued`，上下各 14px），文字是词典的 `chat.continuedAfterBackgroundTask`
  （「Continued after a background task」/「后台任务完成后接着说」），免得看起来像同一个回答出现两次。还在等任务（`'waiting'`）或还没有任何
  文字与工具调用的那一行不画，有了内容才出现；判定是纯函数 `continuesAfterBackgroundTask(previous, message)`。自己用 `#message` 插槽画消息时也照样有。
- **只压缩了上下文的一轮**（外部 CLI 上发 `/compact`）：回答没有文字，`status` 为 `'compacted'`，`compaction` 带压缩前后的 token 数；
  `TThreadMessage` 只显示一行安静的「对话已压缩」，没有复制、评分、重新生成；刷新之后从线程读回的那条也是这个状态。
- **流断了不算这一轮失败**：手机把后台页面冻住、网络掉线时流会断，而服务端可能已经答完、或者还在答。`useChatThreads` 不就此判失败，
  先问服务端：外部 CLI 运行从收到的最后一个事件之后接上（还在跑就接着长，早已结束就补齐剩下的部分并收尾）；没有运行时重读线程，
  线程里已有这一轮的回答就照服务端的样子显示，线程上还有这一轮的运行就接上它。页面还在后台时等它回到前台再问；问不到时隔 2 秒再问一次，
  仍问不到才在那条回答上显示网络错误，之后页面重新显示（`visibilitychange` / `pageshow`）、恢复联网（`online`）、获得焦点、
  再次选中这个会话（例如推送通知的链接打开它）或按重试时都会再问。**重试先问服务端**：服务端已有回答或运行还在跑，就不会把这一轮再跑一遍；
  只有服务端确认既没有回答也没有运行时才重新生成。另外，页面在后台待了 5 秒以上、回到前台时还在流式输出的运行会从最后一个事件之后重新接上
  （冻住的连接可能已经死了却不报错；接上不会重复任何内容）。消费方什么都不用做：用 `useChatThreads` 就有；自己用 `streamChat`
  接流的消费方照此办理，网络错误（没有 HTTP `status` 的 `Error`）与服务端给出的失败要分开对待。
- **编辑与重新生成**走后端自己的回退（`editMessageId` / `regenerate`）：服务端线程与外部 agent 的会话丢掉被替换的那几轮，而不是多出一份重复的提问。
  `enableEdit`（默认关）让用户消息带一个编辑按钮，改完发出的是 `edit(messageId, content)`。编辑框占满对话列宽、随文字增高（超过 480px 后在框内滚动），
  回车保存、Shift+回车换行、Esc 取消。有回答在跑时编辑按钮停用，但它那一行留在原处（拿掉再放回会让整段对话在每轮开始和结束时各跳一次）。
  线程上有回答在跑时服务端拒绝（409），界面会把转录重新读一遍。
- **每轮元数据**：`send(text, files, { metadata })`（`regenerate` / `editMessage` 同样收 `{ metadata }`）随这一轮带一小组值给后端（字符串键、JSON 值，后端限制个数与大小），
  例如用户这一轮点名的那几台设备的 id。重新生成不带它时后端沿用那条消息存下的那份；消息上读回为 `ChatMessage.metadata`。
- **对话来源**：`send(text, files, { origin: 'Card' })` 开始一段新对话时声明它从哪来（发 `ChatRequestDto.origin`，存成线程的 `origin`），
  侧栏的乐观行立刻带上它，图标不等服务端。只有新对话的第一轮带它，续接、重新生成与编辑不带。服务端只接受应用允许客户端声明的值
  （`AI:Chat:AllowedThreadOrigins` 或应用自己的 `IThreadOriginPolicy`），否则这一轮失败，错误码 `AiChatErrorCodes.ThreadOriginNotAllowed`。
  画成图标见下文「对话来源图标」。
- **用量上限**：外部 CLI 背后的账号到了用量上限时，这一轮的回答以 `status = 'usage_limit'` 结束，`TThreadMessage` 把它画成一条单独的提示，
  CLI 报了重置时间就说「大约几点恢复」（`usageLimitResetsAt`）。自己画消息的消费方看 `status` 与 `usageLimitResetsAt`；错误码是 `AiChatErrorCodes.CliUsageLimit`（`@tnzi/core/services/ai`）。
- **错误码**：`onError(message, error)` 的第二个参数是 `ChatThreadsError`：`operation`（在做什么：`load-threads`、`delete-thread`、`attachment`、`stop-run` …）、
  服务端的 `code`（`errorCode`）与 `details`（`errorDetails`）、HTTP `status`。一轮回答失败不走 `onError`，写在那条回答上：`status: 'error'`
  加 `error`、`errorCode`、`errorDetails`（用量上限同样带 `errorCode`）。想用自己的文案时给 `describeError`，按码返回一句话，
  `onError` 收到的与回答上显示的都用它；返回 `null` / 空串保留原句：

  ```ts
  useChatThreads({ /* ... */, describeError: (e) => (e.code ? myCopy[e.code] : null) })
  ```

  输入框自己拒掉的附件（类型不在 `composerAccept` 里、超过大小）走 `attachments-rejected`；发送时 `useChatThreads` 拒掉的附件
  （`operation: 'attachment'`）在 `details` 里带 `reason`（`too-large` / `unsupported-type` / `upload-failed`）与 `fileName`，上传失败时还带上传接口的码。
- **附件与工具图片**：存档的附件与工具结果里的图片要用登录态的客户端读，`<img src>` 做不到，所以消息渲染器经 `resolveAttachmentUrl` 要一个 object URL。
  不给它时，存档的图片只显示一个占位框（悬停提示读不到）。

**回答里的链接**：回答 markdown 里指向别的站点的链接（引用的来源、「Sources」列表、正文里自动识别出的网址）在新标签页打开，
带 `rel="noopener noreferrer"`，点开来源不会把对话页换掉；桌面壳里新标签页链接交给系统浏览器。指向本站同源地址的链接（站内页面、相对路径、页内锚点）
与 `mailto:` / `tel:` 原地打开，怎么跳由产品自己的页面决定。给了自定义 `markdownIt` 实例时这条规则不会加上去（与图片规则相同）。

**工具调用读成回答里的一行**：中间没有回答文字的相邻工具调用（同一位置的、只隔着空行的、内置引擎那种不报位置的）合成一段，画成一行安静的次要文字，
不带外框与底色，行尾一个箭头。一次调用也是一段，所以不管用了几个工具，回答读起来都一样；从历史打开的回答与刚流完的长得一样。

- **进行中**（回答还在流，这一段后面还没写出文字）：行首是一个工作标记，文字是最新一步正在做的事（「Searching the web」「Reading a page」），
  换一步时文字滑入（系统设置了减少动态效果时不动）。
- **完成后**：按种类概括整段，次数多的种类在前（「Read 5 pages, searched the web」「读取了 5 个网页，搜索了网页」），最多点名三种，其余说成「3 more」。
  没有标记、没有耗时。有一步失败、且这一段没能越过它时，行首换成失败标记，失败的部分用危险色单独说出来（「…, couldn't read a page」；多步失败说「2 steps failed」）；
  失败之后同一种类（`kind`）又有一步完成的，算作已重试越过：照常的次要文字说「A step retried after an error」「2 steps retried after errors」，不带标记、不用危险色，
  展开后那一步仍显示为失败。之后只有别的种类成功不算越过（那只说明这一段继续了，不说明失败的那件事做成了）；
  回答被停下时没跑完的说「1 not finished」。
- **展开**：点这一行，下面一个细边框的框，一步一行、行间细线；每行是这一步自己的视图，有详情的照样在原地展开（搜索那一行展开是结果列表）。
  只有一步的那一段，点开就直接是这一步的详情，不用点两次。
- **唯一的进度标记**：一段步骤正在进行、又是屏幕上最后一样东西时，`TThreadMessage` 原本放在下面的「还在写」标记（`agent-mark` 插槽的 `pending`、
  `agentStreamingIcon`、或脉动圆点）移到这一行的行首，下面不再另画一个。插槽收到的仍是 `placement: 'pending'`，另多一个 `inRun: true`，
  按它可以给行首换个尺寸（这一行 20px 高）。打开了署名（`showAgentByline`）时没有那个标记，这一行用自己的转圈。
- **思考不进这一行**：本包拿到的思考是整条消息一块（`message.reasoning`），不知道它发生在哪两次调用之间，所以仍画在回答上方（`TReasoningStage`）。
  后端给思考也报上位置之后才能放进这一行。
- **思考什么时候算「在想」**：思考块（`TThreadMessage` 的 `TReasoningStage`、`TChatMessage` 的 `TReasoning`）只在思考是最新到达的内容时转圈、标题为「Thinking」；
  之后有正文或工具调用到达，它就显示完成，即使整条回答还在流式输出。同一回合里又回去思考，新的思考接在同一块里，块重新转圈，直到下一段输出到达。
  判定在 `useReasoningActivity`（`@tnzi/ui-ai/headless`），自定义消息渲染可以直接用。回答结束、被停下或失败时一律显示完成。

**每一步怎么说：`describe`**。概括要知道每一步的说法。注册工具时把组件换成一个条目（组件可以省略，省略就沿用本包的视图）：

```ts
import type { ToolRendererMap, ToolStepDescriber } from '@tnzi/ui-ai/components'

// 二十几个工具：一张表 + 一个函数。短语按「一行开头」的写法写（「Read a file」「读取了文件」），
// 概括里排在后面的部分本包会把首字母变小写（中文没有大小写，原样）。
const words = {
  read_file: { kind: 'read', running: 'Reading a file', done: 'Read a file', failed: "Couldn't read a file", many: 'Read {count} files' },
  grep: { kind: 'search', running: 'Searching files', done: 'Searched files', failed: "Couldn't search files" },
  run_command: { kind: 'run', running: 'Running a command', done: 'Ran a command', failed: 'A command failed', many: 'Ran {count} commands' },
  send_file: { kind: 'send', running: 'Sending a file', done: 'Sent a file', failed: "Couldn't send a file" },
} as const

const describe = (name: keyof typeof words): ToolStepDescriber => (call) => {
  const w = words[name]
  return {
    kind: w.kind,
    running: w.running,
    done: w.done,
    failed: w.failed,
    many: 'many' in w ? (count) => w.many.replace('{count}', String(count)) : undefined,
    subject: (call.input as { path?: string; command?: string })?.path ?? null,
  }
}

export const TOOL_RENDERERS: ToolRendererMap = {
  read_file: { component: ToolCard, describe: describe('read_file') },
  grep: { component: ToolCard, describe: describe('grep') },
  run_command: { describe: describe('run_command') },      // 不给组件：用通用视图，行上就是这里的说法与主题
  take_screenshot: ScreenshotCard,                          // 只给组件也照旧可以
}
```

| 字段 | 作用 | 不给时 |
|---|---|---|
| `running` / `done` / `failed` | 这一步进行中、完成、失败时的说法，整句的开头形态 | 按工具名：「Using Read File」「Used Read File」「Read File failed」 |
| `many(count)` | 同一种类完成了好几步时的说法（「Read 5 pages」） | 好几步也只说一次 `done`（「Searched the web」），适合没人数的步骤 |
| `kind` | 概括里按它计数与合并：`read_file` 与 `browser_read` 都给 `read`，三次就是「Read 3 files」 | 工具名（去掉 `mcp__服务器__` 前缀） |
| `subject` | 对什么做的（路径、查询词）：通用视图那一行的主题 | 入参里第一个短字符串 |

`describe` 可以是一个固定对象，也可以是函数 `(toolCall, messages) => …`（`messages` 是本包当前语言的词典；产品通常读自己的词典，切换语言时会重新调用）。
函数抛错或返回不可用的值时退回默认说法，不会把回答弄坏。产品自己的组件行上写什么由组件自己定（`TToolStep` 的 `label`），`describe` 只管概括与通用视图。

**留在外面的步骤：`alwaysVisible`**。有的步骤不能收进折叠里，比如交给用户一个文件、带「打开 / 下载」：

```ts
send_file: { component: FileCard, describe: describe('send_file'), alwaysVisible: (call) => !call.error },
```

- 这样的步骤整个显示在这一段的那一行下面，照样计入概括；展开的框里它只占一行说法（不重复画卡片）。函数形式按每次调用判断（例如只让成功的那次留在外面，失败的照常折叠）。
- 一段里全是这种步骤时没有那一行，直接是卡片。
- 用 `TToolStep` 画的步骤不必整个留在外面：它的 `after` 插槽（交出的文件、截图缩略图）在这一段里会自动移到这一行下面，收起时也看得见。通用视图交出的文件就是这么留在外面的。

Claude Code CLI 的内置工具 `WebSearch` 与 `WebFetch` 默认有自己的视图与说法（中英两种界面语言）：搜索显示查询词，展开是找到的网页（标题 + 站点，新标签页打开），
概括里不计数（「Searched the web」）；读取显示「Read 站点」，展开是网页地址与 CLI 读出的内容（折叠），概括里计数（「Read 5 pages」）。
它们按工具全名精确匹配，经 MCP 到达的同名工具（`mcp__x__WebSearch`）不算。CLI 的结果是文字而不是数据，换个版本措辞可能变：读不出网页列表时显示原文，不会报错。

agent 转向人的三个工具 `AskUserQuestion`、`EnterPlanMode`、`ExitPlanMode` 也有自带的说法（用通用视图，中英两种）：「Asking you a question」/「Got your answer」、
「Started a plan」、「Waiting for the plan to be approved」/「Plan approved」，主题是第一个问题或计划的标题。被退回的计划、没回答的问题在 CLI 那边是一次
失败的调用，这里读作「Plan sent back」/「Question left unanswered」，画成完成而不是失败，也不算进概括里的失败。产品自己的工具要同样处理，在说法里给
`declined`（失败的结果其实是人的回答时用的那句）；`isDeclinedStep(step)` / `shownStepState(step)` 是判断与画法用的纯函数。

**按工具名换渲染器**：产品想把自己的工具画成自己的样子时按工具名注册组件（或上面的条目）：

```ts
defineChatApp({ /* ... */, toolRenderers: { take_screenshot: ScreenshotCard } })   // 整个应用
// 或者只给一个外壳：<TChatApp :tool-renderers="{ take_screenshot: ScreenshotCard }" />
```

组件收到 `toolCall`（`ToolCallInfo`：`name`、`input`、`status`、`result` 内容块、`durationMs`）与 `streaming`。名字先按全名匹配，再按最后一个 `__` 之后的部分匹配
（经 MCP 到达的工具带服务器前缀，如 `mcp__acme__take_screenshot`），再看本包自带的（`BUILT_IN_TOOL_RENDERERS`），都没有的用通用视图（`TToolCallCard`）。
外壳上的注册覆盖应用级的同名注册；产品的条目与自带的按字段合并（只换组件就保留自带的说法，反之亦然），想要回通用视图就注册 `{ WebSearch: TToolCallCard }`。

自己的工具视图用同一个外壳画，与自带的看起来是一家（都从 `@tnzi/ui-ai/components` 导出）：

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { TToolStep, TToolOutput, toolCallState, toolResultText, type ToolRendererProps } from '@tnzi/ui-ai/components'

const props = defineProps<ToolRendererProps>()
const state = computed(() => toolCallState(props.toolCall, props.streaming))
const command = computed(() => (props.toolCall.input as { command?: string } | undefined)?.command ?? null)
const output = computed(() => toolResultText(props.toolCall))
</script>

<template>
  <TToolStep :state="state" label="Ran" :subject="command" mono icon="lucide:terminal" :expandable="!!output">
    <template #meta>on Office-PC</template>
    <TToolOutput :text="output" />
    <template #after><!-- 文件卡片、截图缩略图：不随展开收起，在一段里移到那一行下面 --></template>
  </TToolStep>
</template>
```

- `TToolStep`：`state`、`label`（做了什么）、`subject`（对什么做的，一行截断；`mono` 用等宽字）、`icon`（完成后的图标）、`expandable`
  （默认看有没有默认插槽）、`defaultOpen`，事件 `toggle(open)`。插槽：默认插槽是展开的内容；`meta` 是主题之后的短文字（它在这一行的按钮里，只放文字）；
  `after` 始终可见；`icon` 换掉开头的标记。在一段步骤里它自动画成框里的一行（36px，详情原地展开，不再套一层框）。
- 框里的三种部件：`TToolFields`（一行一个「名称 值」，由 `toolFields(value)` 生成；网址是新标签页链接，长文本单独成块并折叠）、
  `TToolLinkList`（网页列表：标题、站点，新标签页打开；不加载网站图标，回答里不让任何远程资源自己加载）、
  `TToolOutput`（文本：默认等宽在内凹底上，`mono: false` 为正文；超过 `foldLines` 行，默认 12，折叠并给出「展开全部 N 行」）。
- 读结果的工具函数：`toolResultText(toolCall)`（文本块或 `output`）、`toolResultView(text)`（链接 / 一行一个的值 / 文本）、
  `parseWebSearchResult(text)`、`webFetchTarget(input)`、`webHref`、`linkDomain`。
- 自己拼外壳时：`TToolRun`（`toolCalls`、`streaming`、`active`，插槽 `mark` 是进行中的标记）画一段；`replyParts(segmentReply(content, toolCalls))`
  把回答切成文字与一段段调用；`summarizeToolRun` / `runningRunLabel` / `describeToolStep` / `resolveToolEntry` 是它们用的纯函数。

**文件卡片的链接**：工具交给用户的文件（结果里的文件块，带 `name` 与 `uri`）画成文件卡片。`http(s)` 地址与站内相对路径默认直接链过去；
用自己方案的地址（`storage://…`、一个文件 id）由 `fileHref` 换成浏览器打得开的地址，返回 `null` / `undefined` 用默认：

```vue
<TChatApp :file-href="(file) => file.uri?.startsWith('storage://') ? `/files/${file.uri.slice(10)}` : null" />
```

**未登录先打字**：`defineChatApp({ guestWelcome: true })` 加一个欢迎页（默认 `/welcome`），未登录的访客打开首页时落到它而不是登录页。
访客先打字，发送时消息存在这个标签页里（`sessionStorage`，30 分钟内有效）并转去登录；登录回来之后 `useChatThreads` 把它当作第一轮发出去
（`sendWelcomeMessage: false` 关掉这一步）。给了 `agentId` 时它等到 `agentId()` 交出一个 agent 才发（读的是响应式状态，启动之后才解析出来的
agent 照样等得到），也不会压在一轮正在跑的回答上；没给 `agentId` 时启动即发。agent 可能一直空着的产品自己发：
`sendWelcomeMessage: false`，再 `takeWelcomeMessage()` 加 `send()`。文件不跨这一趟登录保存，所以欢迎页只收文字。文案经 `guestWelcome: { greeting, subline, placeholder, hint }` 改，
整页可换成自己的组件（`component`，它要调 `stashWelcomeMessage` 再去登录）。浏览器里与桌面 webview 里行为相同。

### 对话来源图标：`threadIcon`

线程带着服务端存下的来源（`ThreadItem.origin`，即 `AgentThreadDto.origin`：应用定义的值，例如 `Email`、`Schedule`、`Card`），
给 `TChatApp` 一个把线程映射成图标的函数，侧栏的会话列表（`TThreadList`）、收起侧栏时图标栏旁的会话面板（`TRailThreadList`）
与查看全部页（`TRecentsPage`，翻页加载的每一页都经过它）用同一个映射画图标：

```vue
<script setup lang="ts">
import type { ThreadIconResolver } from '@tnzi/ui-ai/chat';

const ORIGIN_ICONS: Record<string, { icon: string; label: string }> = {
  Email: { icon: 'lucide:mail', label: '由邮件规则发起' },
  Schedule: { icon: 'lucide:alarm-clock', label: '由定时任务发起' },
  Card: { icon: 'lucide:sparkles', label: '从卡片发起' },
};

// 手动开始的对话没有 origin：返回 null，那一行不画图标、也不留空位
const threadIcon: ThreadIconResolver = (thread) => (thread.origin ? ORIGIN_ICONS[thread.origin] ?? null : null);
</script>

<template>
  <TChatApp :threads="chat.threads.value" :recents="chat.recents" :thread-icon="threadIcon" />
</template>
```

- 返回 `{ icon, label }`：`icon` 是 Iconify 名字（图标要能离线取到，见「图标：不向第三方取」），`label` 是整行按钮的悬停提示，
  也排在行的无障碍名称最前面：读屏读整行时是「由邮件规则发起 发票…」（按钮里一段只给读屏的文字；行尾的图标本身对读屏隐藏，不读两遍）。
  `label` 为空时图标只是装饰（没有提示，名称里也没有它）。
- 返回 `null` / `undefined`（或空的 `icon`）：没有图标，行尾平时空着。映射函数抛错只让那一行没有图标。
- **图标在行尾，就在「…」行操作菜单的位置**，所以每一行的标题都从同一个 x 开始（侧栏里是导航图标那一列），有没有来源都对齐。
  平时有来源的行在行尾显示图标；指针悬停、键盘焦点落在行内、菜单打开，以及触屏（本来就一直显示「…」）时，「…」换到图标的位置，用法与原来一样。
  没有「…」的地方图标一直在：收起侧栏的会话面板（只用来切换，没有行操作）、关掉了全部行操作的 `TThreadList`；正在打开的那一行行尾是转圈。
- 图标比标题小一号、用次要文字色，不抢标题，也不随选中、悬停的底色变化。侧栏里它与「…」同一个格子（`--tnzi-ai-row-action-size`），
  查看全部页里在最近活动时间之后、同样占「…」的格子，会话面板里离标题至少 8px、靠行尾。
- 三个组件各自也收 `threadIcon`，自己组装列表的直接传给它们；`TThreadList` 的 `#row` 插槽多一个参数 `icon`（已解析好的 `{ icon, label }` 或 `null`）。
  `#row` 只换行按钮里的内容，行尾的图标照旧由列表画，插槽里不要再画一次；插槽换掉了默认内容，读屏用的来源文字也随之没有，需要时自己放。
  画图标的组件是 `TThreadIcon`（`spec` 属性；`decorative` 让它对读屏隐藏、只留提示），解析函数是 `resolveThreadIcon(resolver, thread)`，
  都从 `@tnzi/ui-ai/components` 导出。
- 不经 `useChatThreads`、自己把 DTO 转成 `ThreadItem` 的，用 `toThreadItem` / `toThreadItems`（`@tnzi/ui-ai/adapters`）会带上 `origin`（以及下文的 `running` / `unread`）。

### 对话待你处理、在跑、有未读

`ThreadItem` 有三个状态：`awaitingOwner`（这条对话的一轮在等你：权限请求、问题、应用自己的确认卡片）、`running`（此刻有一轮在服务端跑）与
`unread`（有一段回答主人还没看过）。三个列表在行尾、来源图标的那个格子里画它们（`TThreadStatus`）：待你处理是一只强调色的举手，在跑是一个细的转圈，
未读是一个强调色的实心点；同时有几个时只画最前的那个（举手 > 转圈 > 点）。有状态的行平时不画来源图标，
悬停、键盘焦点、菜单打开时照样换成「…」；触屏上「…」一直在，状态就挪到「…」的前面。状态本身对读屏隐藏，行的名称在标题后面接一个词
（「Needs you」/「Working」/「Unread」，词典 `recents.threadAwaitingOwner` / `recents.threadRunning` / `recents.threadUnread`）。`prefers-reduced-motion` 下转圈停住。
前两个状态同时决定行在列表里的层（见上文「顺序」）。

数据从后端来（`AgentThreadDto.awaitingOwner` / `running` / `unread`，判定见 `docs/modules/ai.md`「线程的『在跑』与『未读』」与「线程『待你处理』与列表顺序」）。
应用自己的确认机制要让对话显示待你处理，在后端注册 `IThreadAwaitingOwnerProvider`（见升级说明）。用 `useChatThreads` 的应用什么都不用接：

- 它带一个 `useThreadAttention`（`chat.attention`），读 `POST /api/threads/attention`：有对话在跑或待你处理时每 5 秒（`ATTENTION_RUNNING_POLL_MS`），否则每 30 秒
  （`ATTENTION_IDLE_POLL_MS`），页面重新可见、获得焦点、恢复联网时立刻读一次，页面隐藏时不读；一轮在本页开始（运行有了 id）或结束时也读一次。
  结果叠到侧栏列表与最近对话页的行上，并按上文的顺序重排；本页正在流式的那一轮（包括内建 agent 的）也算在跑，屏幕上那条对话出现审批卡片
  （包括 `approvalSources` 里应用自己的卡片）即算待你处理，卡片来去时立刻重读一次。
- 打开一条对话、对话在屏幕上而它的一轮结束、页面回到这条对话时，它把这条对话标为已读（`POST /api/threads/{id}/read`），屏幕上开着的对话从不显示未读。
- `useChatThreads({ attention: { runningPollMs, idlePollMs } })` 改间隔（0 = 不定时读，只在上面这些时机读）；`attention: false` 关掉，行上只留列表读到的状态，也不标已读。
- 后端没有这个接口（404 / 405 / 501）时只问一次，之后计数一直是 0。

**放到模式切换或导航上的汇总**：`chat.attention.runningCount` / `unreadCount`（`listAgentIds` 限定的那些 Agent 的全部对话，不受列表分页影响）。
这个模式的对话不在屏幕上时（比如用户在另一个模式），自己起一个，用同样的 Agent 过滤：

```ts
import { useThreadAttention } from '@tnzi/ui-ai/headless'
import { useThreadApi } from '@tnzi/core/services/ai'

const chatAttention = useThreadAttention({ threadApi: useThreadApi(http), agentIds: () => chatAgentId.value })

const modes = computed(() => [
  { value: 'chat', label: 'Chat', icon: 'lucide:message-square',
    running: chatAttention.runningCount.value > 0,
    dot: chatAttention.unreadCount.value > 0 ? 'warning' : undefined, dotLabel: 'New replies' },
  { value: 'code', label: 'Code', icon: 'lucide:code', running: codeRunning.value },
])
```

`useThreadAttention` 还给 `awaitingOwnerCount` / `awaitingOwnerIds`、`runningIds` / `unreadIds`（各至多 200 个）、`loaded`、`active`（有待你处理、在跑或未读）、`refresh()`、`markRead(id)`（先在本地去掉、再告诉服务端，
标记之前发出的读取不会把它带回来），`immediate: false` 时等 `start()`；在组件作用域里调用会随作用域停下，别处调用用 `dispose()`。

## 输入框上方的卡片：批准、提问、计划

外部 agent 的一轮在等用户时（「先问我」档要做一件不自动放行的事、agent 问一组问题、交出计划等批准），`TChatApp` 在对话输入框正上方画一张卡片，
倒数到期限，期限一到按拒绝收起。用 `useChatThreads` 的应用什么都不用接：

- **来源**：流上的 `permission_request` 一到就出卡片，同一请求的 `permission_resolved`（这里答的、别处答的、超时、运行结束）一到就收起；
  打开一个还有运行在跑的会话、以及这一轮运行期间每 `approvalPollMs`（默认 2 秒，0 只在打开时问）一次，问 `GET chat/threads/{threadId}/permissions`，
  所以刷新页面、从中途接上的流之后仍能找回还在等的卡片。这一轮结束或换了会话，卡片清空。
- **回答**：权限卡的按钮是允许一次（主按钮）、总是允许（请求给了才有）、拒绝；提问卡每题单选或多选，另有「其他」写自己的回答，
  每题都有回答才能提交，也可以不回答；计划卡把计划按 Markdown 显示在自己滚动的框里，批准、批准且改文件不再问（计划档启动的运行才有）、退回修改
  （打开一个框写意见，可以不写，焦点进框）。回答走 `POST chat/runs/{runId}/permissions/{requestId}`，期间按钮等着，成功后卡片立刻收起；
  请求已经不等了（409 等）时卡片收起并提示一句；别的拒绝（比如回答不合格）留着卡片并提示原因。提示走 `useToast`。
- **布局**：卡片排在 `#thread-composer-top` 的内容之后，多张从旧到新、相隔 8px，超过窗口高度 40% 时在自己里面滚动，下面还有内容时末几行渐隐。
  卡片区是 `role="region"`（名称是词典的 `approvals.region`），每张卡是带名称的 `role="group"`。手机上（外壳的窄屏）有卡片在等时输入框只留工具条
  （停止、模型选择这些），文字框与附件条收起，把地方让给卡片；没有卡片时恢复。宽屏不受影响。
- **文案**：词典 `approvals.*`（en / zh-cn）。自己写了 `AiLocaleMessages` 类型词典的，要补上这一节。

**`TChatApp` 不传 `approvals` 时用哪个队列**：每个在组件 setup 里调用的 `useChatThreads` 都向那个组件登记自己（它的 `messages` 与队列），
同一个组件调用两次（一个外壳里既有对话又有编码 agent，各一个实例）两个都在，不会后一个盖掉前一个。`TChatApp` 按下面的顺序选：

1. 它收到的 `messages` 就是哪个实例的 `messages.value`（同一个数组，即 `:messages="chat.messages.value"`），就用那个实例的队列：屏幕上是哪段对话，卡片就是哪段的；
2. 都对不上（页面传的是筛选或映射过的副本）而只有一个实例，用那一个；
3. 有几个实例又都对不上，不显示卡片，而不是显示另一段对话的。

所以一个组件里有两个实例时，**把正在显示的那个实例的 `messages.value` 原样传给 `TChatApp`**，或者干脆显式传 `:approvals="current.approvals"`，
后者总是优先。不在同一个组件树里（实例放在 store 里、自己管流）时也传 `:approvals`。`TChatApp` 没有 `message-queue` 一类的属性：
运行中排队的消息是页面自己的（`queueWhileRunning` + `queue` 事件），不经过这里，只有审批卡片是按实例选的。
自己搭外壳的组件要同样的选法时用 `@tnzi/ui-ai/headless` 的 `CHAT_THREADS_KEY`（`inject`）与 `pickChatApprovals(registry, messages)`。
`:approvals="null"` 关掉卡片。

**追加自己的卡片种类、提供自己的待处理来源**：应用自己的「等用户确认」（发一封邮件给别人、发布上线之前）作为一个来源交给 `useChatThreads`，
它与外部 agent 的请求排在同一列卡片里、一起轮询、一起在手机上收起输入框：

```ts
import type { ApprovalSource } from '@tnzi/ui-ai/headless'

const confirmations: ApprovalSource = {
  id: 'confirm',
  // 这个会话现在在等什么。key 在所有来源之间唯一（自己加前缀），kind 是自己的种类名
  load: async (threadId) =>
    (await api.pending(threadId)).map((c) => ({ key: `confirm:${c.id}`, kind: 'confirmation', expiresAt: c.expiresAt, createdAt: c.createdAt, data: c })),
  // 回答它；已经不等了返回 'closed'，其他失败抛出带说明的 Error（卡片留着、提示原因）
  answer: async (item, answer) => {
    const result = await api.confirm((item.data as Confirmation).id, answer.optionId === 'approve', answer.code)
    if (result.code === 409) return 'closed'
    if (!result.succeeded) throw new Error(result.message)
  },
}

const chat = useChatThreads({ http, chatApi, threadApi, approvalSources: [confirmations] })
```

卡片长什么样由 `describeApproval` 给（`ApprovalCardModel`：`title`、`summary`、`warning`、`fields`（带 `more` 可展开的明细）、`detail`、`choices`、`code`、`expiresAt`），
按钮的 `id` 原样作为 `answer.optionId` 交回来源，`code` 要一串数字时主按钮等输入够位数，数字随 `answer.code` 交回。
返回 `null` 的项目：内置种类（`permission` / `question` / `plan`）用框架的卡片，别的种类交给 `#approval-card` 插槽
（插槽属性 `item`、`busy`、`answer(reply)`、`remove()`、`expiresText(time)`）。给内置种类返回模型就是换掉框架的措辞（比如按工具名写自己的标题）。

```vue
<TChatApp :describe-approval="(item) => item.kind === 'confirmation' ? confirmationCard(item, t) : null" ... />
```

单独用卡片时：`TApprovalCard`（`card`、`busy`、`expiresText`，事件 `choose(choiceId, code?)`、`expired`；默认插槽在正文与按钮之间）、
`TQuestionCard`（`card`、`labels`，事件 `submit(answers)`、`decline`、`expired`）、`TPlanCard`（`card`、`labels`，事件 `choose(optionId, message)`、`expired`）、
`TPendingApprovals`（`queue`、`describe`，插槽 `#card`）。无渲染的部分在 `@tnzi/ui-ai/headless`：`createApprovalQueue`（`follow`、`refresh`、`push`、`remove`、`answer`）、
`createRunRequestSource`、默认卡片模型 `permissionCardModel` / `questionCardModel` / `planCardModel`、`permissionChoices`、`planChoices`、`questionAnswers` 等纯函数，
以及 `vScrollHint`（长内容的框还有没显示完的部分时带 `data-more-below`，样式据此让末几行渐隐）。

## 排队的消息：运行中写下的消息在服务端排队

外部 agent（绑定了外部 CLI 运行时）的一轮常常跑好几分钟。用 `useChatThreads` 的应用什么都不用接：
这一轮还在跑时，输入框的主按钮变成「排队」（停止挪到它左边），写下的消息（可带附件）存在服务端（`chat/threads/{threadId}/queue`）。
这一轮收得下运行中的消息时（claude 走 stream-json），服务端马上把它交给正在跑的 agent，agent 在两步之间收下、接着做，什么都不停；
收不下时这一轮结束后线程里等待的消息合成一轮发出。队列不在页面里：刷新页面不会丢，没有页面开着也照样发。服务端的规则见 `docs/modules/ai.md`「线程的消息队列」。

- **排队**：`useChatThreads().send()` 在外部运行进行中（`messageQueue.available`）把消息排进队列，带上这一轮的模型（`model()`）与每轮元数据（`send` 的 `metadata`），
  而不是像以前那样什么都不做。`TChatApp` 的排队按钮（与回车）因此发出 `send`，不是 `queue`。几条消息合成一轮时，每轮元数据在服务端合并：
  默认后写的为准，**权限一类的键要在服务端声明取最严值**（`AI:Chat:Queue:StrictestValues` 或应用自己的 `IChatQueueMetadataMerger`，
  见 `docs/modules/ai.md`「线程的消息队列」），否则先排的「先出计划」会被后排的「全权」盖掉。**附件**与普通发送同一套规则（`inlineFiles`、
  `maxAttachmentBytes` / `maxFileAttachmentBytes`），以内联数据随消息排进队列（经引用上传的文件排不进去）；服务端存下，刷新后照样列出。
  被拒的附件经 `onError` 报（`operation` 为 `attachment`），排满 `AI_QUEUE_FULL` 与服务端的其他规则报 `queue`，文字回到空着的输入框。
  内建 agent 的一轮挂在请求上、服务端看不见，它进行中照旧只有停止。
- **列表**：`TChatApp` 在输入框上方列出排队的消息（`TQueuedMessages`），每条带着它的附件名。还等着的与扣住的可以编辑（就地改，回车保存、Esc 取消，
  焦点回到编辑按钮）、移除、立即发送。运行中的按钮说的就是它会做的：服务端说这条交给正在跑的 agent 不必停（`sendNowStopsTurn` 为 false）时是「立即发送」
  （提示「马上交给助手，它做完当前这一步就会读到」）；收不下时是「停下并发送」（方块图标，提示「这一轮运行中收不了消息：停下它，马上发这一条」）。停下一轮本来是停止按钮的事，
  所以这种情况不偷偷把立即发送变成停止，而是把按钮叫成它真正做的事。
  每条下面一行说它的处境：下一条发出、正在发出、已交给助手（它做完当前这一步就会读到）、这一轮被停下了没有发出（扣住）、
  没发出去及原因（扣住，原因按 `failureCode` 用词典的说法，认不出的码显示服务端的说明）。交给助手的没有操作；助手收下之后它就是对话里的一条用户消息，从列表里拿掉（没有别的消息时整张卡片不出现）。扣住的消息只有点「立即发送」才会发。
  标题旁的提示按等着的消息说「助手会在两步之间读到」或「这一轮结束后一起发出」。
- **回答中间的插话**：助手收下一条排队的消息时，聊天流来一个 `user_message` 事件，`useChatThreads` 在那里收起当前的回答（换上服务端存下的 id）、
  插入用户的话、之后的文字开一条新的回答：与刷新后线程里的次序一样。
- **跟上服务端发出的一轮**：收不下的一轮结束后，列表里的消息由服务端发出，那一轮不是这个页面发起的。队列在有消息等着（或在助手手里）时每 `messageQueuePollMs`
  （默认 2 秒，0 只在打开会话与一轮结束时读）读一次，看到消息走了就调 `refreshThread()` 接上那一轮的流（助手收下的消息不算：服务端在那一轮结束时删掉它的行，不是新的一轮）。
  服务端常常在上一轮结束后几十毫秒就发出下一轮，这时本页那一轮的流可能还没结束：看到消息走了的信号先记着，等本页这一轮结束再跟上，页面中途打开（接上运行中的一轮）时也一样；
  `refreshThread()` 在有一轮正在流式输出或正在打开对话时被调用，也是等页面空下来再做。一轮结束后的头几次读间隔 `QUEUE_AFTER_TURN_MS`（400 毫秒），
  服务端对等着的消息做了什么（发出，或因为这一轮被停下而扣住，`failureCode` 为 `AI_QUEUED_TURN_STOPPED`）马上显示出来。
- **布局**：输入框上方从上到下是 `#thread-composer-top` 的内容、排队的消息、等用户回答的卡片。卡片离输入框最近：它们是现在就要回答的，
  手机上还会顶替输入框；有卡片在等时排队的消息收成标题一行（「排队中 N 条」与展开按钮），把地方让给卡片（输入框上方这一带最多占窗口高度 40%，超出在自己里面滚动，卡片不能被挤出去），用户可以自己展开。
  列表是 `role="region"`（名称是词典的 `queue.region`），超过窗口高度约三分之一时在自己里面滚动。颜色只用 `--tnzi-ai-*` 变量，浅色、深色都跟着主题。
- **文案**：词典 `queue.*`（en / zh-cn）。自己写了 `AiLocaleMessages` 类型词典的，要补上这一节。

`useChatThreads` 把队列和审批卡片一起登记在调用它的组件上（`CHAT_THREADS_KEY`），`TChatApp` 按它显示的 `messages` 选出同一个实例的队列，一个组件里有两个 `useChatThreads` 时也不会拿错；不在同一个组件里、或者传给 `TChatApp` 的不是 `chat.messages.value` 本身时，传 `:message-queue="chat.messageQueue"`。
`:message-queue="null"` 关掉列表与服务端排队（自己做队列的应用，接着用 `queueWhileRunning` 与 `queue` 事件）。

单独使用：`TQueuedMessages`（`queue`、`folded`）；无渲染的部分在 `@tnzi/ui-ai/headless`：`createMessageQueue({ api: useChatApi(http), onDelivered, onError })`
（`items`、`available`、`running`、`busy`、`follow(threadId, running, serverRun)`、`add`、`edit`、`remove`、`sendNow`、`refresh`、`dispose`），
纯函数 `wentOut`、`waitsToGo`、`queuedLine`、`queuedText`，常量 `QUEUE_POLL_MS`，以及按显示的会话选队列的 `pickChatMessageQueue(registry, messages)`。
`@tnzi/core` 的 `useChatApi()` 有对应的五个方法：`getQueue`、`queueMessage`、`updateQueuedMessage`、`removeQueuedMessage`、`sendQueuedMessageNow`。

## 只读看一个对话：`useThreadReader` 与 `readonly`

一个对话没有人在这里写，只拿来看（无人值守的运行留下的对话，常常不进对话列表，`ThreadListing.Never` / `UntilUserMessage` 也照样按 id 读）：
`useThreadReader` 按 id 读这个对话、往前翻页，接上正在跑的那一轮（从这一轮开头接起，和 Chat 刷新后一样），
之后开始的一轮也会自己发现并接上，消费方不用轮询；一轮结束后，行就是服务端存的样子。

```vue
<script setup lang="ts">
import { useChatApi, useThreadApi } from '@tnzi/core/services/ai'
import { TThreadMessage } from '@tnzi/ui-ai/components'
import { useThreadReader } from '@tnzi/ui-ai/headless'

const props = defineProps<{ threadId: string }>()
const reader = useThreadReader({
  http,
  threadApi: useThreadApi(http),
  chatApi: useChatApi(http),
  threadId: () => props.threadId,
  onError: (m) => toast.error(m),
})
</script>

<template>
  <button v-if="reader.hasOlderMessages.value" @click="reader.loadOlderMessages()">更早的消息</button>
  <TThreadMessage
    v-for="m in reader.messages.value"
    :key="m.id"
    :message="m"
    readonly
  />
</template>
```

- **它只读**：返回值里没有 `send` / `regenerate` / `editMessage` / `abort` / `inputText`；对话列表、标题、在跑与未读的汇总和已读标记、
  待处理的批准、排队的消息、访客欢迎消息一概不读不写，也不向组件树提供对话（页面里的 `TChatApp` 或输入框找不到它）。
  它发的请求只有这个对话的消息（`getMessages`）、显示时的附件（`resolveAttachmentUrl`）、它在跑的运行（`getActiveRuns`）和运行的流。
- **跟随**：没有在流的时候每 `followPollMs`（默认 `THREAD_FOLLOW_POLL_MS`，3 秒；0 = 只在打开、`follow()`、页面回来时问）问一次这个对话有没有在跑的运行，
  有新的就先重读消息（这一轮的提问在里面）再从头接上它的流；页面隐藏时不问，回来马上问一次。已经接过的运行在它结束后短暂还被列出，不会被当成新的一轮。
  流断了、页面冻过的恢复与 Chat 一样（同一套流处理）。
- **停止跟随**：`stopFollowing()` 关掉屏幕上的流（服务端那一轮照跑，不取消），回答留着已经到的内容、不再显示在写，也不再找新的一轮；
  卸载时同样。`follow()` 重新读并接上；`refresh()` 按服务端重读（跟随时也接上在跑的一轮）。`follow: false` 开始时就只读一次。
- `threadId` 是响应式的：换 id 换对话（上一个的流放开，不取消）；`null` 什么都不显示。
- 只有以运行方式跑的轮（外部 CLI agent）能实时跟随；内建 agent 在发起那一轮的请求上作答，它的回答下一次 `refresh()` 时出现。
- 其余返回值：`threadId`（屏幕上的对话，消息到了才设）、`loading`、`isStreaming`、`runId`、`hasOlderMessages` / `loadingOlderMessages` / `loadOlderMessages`、`following`。

**`readonly`**（`TThreadMessage`、`TChatMessage`、`TMessageList`，默认关）：一份记录，不是一个能在里面操作的对话。
失败的回答照样显示失败那一行（为什么失败），但没有 Retry；回答下面没有操作行（复制、重新生成、赞踩，`actions` 插槽也不渲染），
用户消息没有编辑；`TMessageList` 也不在底部给输入框留空白。它不看 `showActions` / `enableEdit` / `showFeedback`。
不加 `readonly` 时一切照旧：只关 `showActions` 仍然保留失败回答上的 Retry。
`TMessageList` 画的是经典气泡布局（`TChatMessage`）；要 Chat 那种布局，像上面那样逐条渲染 `TThreadMessage`。

## 右侧面板：`aside` 插槽

对话旁边需要一块产品自己的面板（本次对话产出的文件、长任务的计划与步骤、代码评审的改动与终端）时，给 `TChatApp` 一个 `#aside` 插槽。框架出外框，页签和内容归产品。没有这个插槽就没有面板：不占位，顶栏也没有开关，已有的接法不受影响。

```vue
<TChatApp :active-thread-id="threads.activeThreadId.value" :aside-default-tab="'artifacts'" aside-label="Workspace">
  <template #aside="{ tab, setTab, close, narrow, threadId }">
    <MyTabs :value="tab" @update:value="setTab" />
    <MyArtifacts v-if="tab === 'artifacts'" :thread-id="threadId" />
  </template>
</TChatApp>
```

- **外框**：宽屏是右侧一栏，左缘有可拖动的分隔条（`role="separator"`，可聚焦；左右方向键调 16px，加 Shift 调 64px，Home / End 到最小 / 最大）。窄屏（`isMobile`）是盖住对话的整层，顶部有返回按钮，Esc 也能关。面板只在对话视图里出现：产品自己的 `view` 页里没有，落地页上默认也没有（见下面的「落地页上的面板」）。
- **按对话记住**：开 / 关、宽度和当前页签按对话（`activeThreadId`）分别记在 `localStorage`（`asideStorageKey`，默认 `tnzi-ui-ai-aside`，`null` 则只在内存里；最多记 200 个对话）。没动过的对话用默认值：`asideDefaultOpen`（默认关）、`asideDefaultWidth`（420）、`asideMinWidth` / `asideMaxWidth`（280 / 720）、`asideDefaultTab`。还没有 id 的新对话共用一个草稿位，第一轮的流说出会话 id 时把状态带过去。
- **插槽参数**：`tab`、`setTab(id)`、`close()`、`narrow`、`fullHeight`（此刻按 `asideFullHeight` 占满高度）、`threadId`。页签 id 由产品定义，框架只负责记住。
- **以代码打开**：`TChatApp` 的模板引用上有 `openAside(tab?)`、`closeAside()`、`toggleAside()`；对话里的渲染器（工具调用行、`message` 插槽里的组件）用 `useChatAside()`（`@tnzi/ui-ai/chat`）：`useChatAside()?.show('changes')`。没有面板时它返回 `null`。状态变化时发 `aside-change`（`{ open, tab }`）。
- **顶栏开关**：`showAsideToggle`（默认开）。想自己放按钮就关掉它，再用上面的方法。
- **落地页上的面板**：`asideOnLanding`（默认关）。打开后新对话的落地页上也有面板：顶栏出现开关，`openAside()` / `useChatAside()` 在落地页上同样生效，外框、拖动调宽、宽度上限与对话里相同，落地页的内容在剩下的宽度里重新居中。落地页的状态（开关、宽度、页签）记在新对话的草稿位，发出第一条消息、对话有了 id 后交给这个对话；切换时面板一直挂着（同一个元素，插槽内容不重挂载），只是旁边的落地页换成了会话。适合在第一条消息之前面板就有内容可看的起始页，例如从项目卡片打开的「项目起始页」：空输入框，右侧是这个项目的文件与历史。

  ```vue
  <TChatApp ref="shell" aside-on-landing :active-thread-id="threads.activeThreadId.value">
    <template #aside="{ threadId }">
      <ProjectPanel :project="currentProject" :thread-id="threadId" />
    </template>
  </TChatApp>
  <!-- 打开项目卡片时：threads.newChat(); shell.value.openAside('files') -->
  ```

  不开时落地页上没有面板也没有开关，`openAside()` 只记下状态、不显示任何东西（与此前相同）。落地页上不显示产物面板（`artifact`）。
- **落地页不是「新建对话」时**：侧栏的新建对话入口在落地页上显示为当前所在（展开的侧栏与图标栏一致）。产品把落地页用作别的东西时（例如上面从项目卡片打开的项目起始页），传 `:landing-is-new-chat="false"`，入口就不再高亮；回到真正的新对话时传回 `true`（默认）。点击入口照常发 `new-chat`。

  ```vue
  <TChatApp aside-on-landing :landing-is-new-chat="!currentProject" ...>
  ```
- **宽度的先后**：面板旁边的对话保底 `min(480px, 60%)` 的可读宽度，窗口变窄时面板先让。全高面板的列最多占主区的 70%
  （`--tnzi-ai-aside-max-share`），在落地页旁边最多 30%（`--tnzi-ai-aside-landing-max-share`：起始页旁边放的通常是文件列表，页面本身才是重点）。
- **占满高度**：`asideFullHeight`（默认关）。打开后宽屏上的面板从主区最顶端开始，占满整列高度；顶栏只横跨对话一栏（有产物面板时连同它），右端的 `#topbar-actions` 和面板开关停在面板左边。适合面板自带一排视图图标的产品（如编码 agent 的改动、终端、预览）。不开时面板在顶栏下面，顶栏横跨整个主区。只在对话视图、有 `#aside` 插槽时生效：产品自己的 `view` 页不受影响，落地页只在 `asideOnLanding` 时受影响；面板关着时布局也已就位，所以开关不会让任何东西上下跳；窄屏仍是盖住对话的整层。面板在 DOM 里的位置不随这个选项或断点改变，所以跨断点不会重挂载插槽内容。面板占满高度时右上角是它的：窗口按钮画在那里的桌面应用，面板内容按 `--tnzi-ai-aside-inset-top` / `--tnzi-ai-aside-inset-right` 让位（见「窗口边角」）。
- **配色**：只用 `--tnzi-ai-*` 令牌，换主题即跟着换。
- 底层积木：`TAsidePanel`（外框）和 `useAsidePanel`（无头状态），自己组装外壳时可单独用。

## 子 agent 与后台任务

外部 agent（Claude Code）用 `Agent` 工具派出的子 agent，和它用 `run_in_background` 起的命令，是 CLI 的**任务**。它们在对话里与别的工具调用一样是一步：
算进那一行的摘要，展开才是逐步列表；子 agent 自己的工作在它自己的视图里，不填进主对话。用 `useChatThreads` 接 `TChatApp`（或直接渲染 `TThreadMessage`）的应用不用接就有前两条；
侧栏列表和「点一个子 agent，侧面滑出它的视图」要在 `#aside` 里放组件（下面的例子）。

- **摘要行**：连续的工具调用收成一行，用自然的计数说发生了什么：「Finished 6 background commands, read 2 files, edited a file, 1 more, screenshot failed」。
  任务按**它自己**的状态算，不按起它的那次调用（后台调用当场返回）：还在跑的说「A background command running」「2 agents running」，被停的说「Stopped an agent」，失败的算进失败。
  Claude Code 的文件与命令工具（`Read`、`Edit`、`MultiEdit`、`Write`、`Bash`、`Grep`、`Glob`）有自己的说法（「read 2 files」「ran a command」「searched the code」），产品注册的同名说法优先。
- **行尾**：不用展开就能看到这几步改了多少行（绿色 `+12`、红色 `-3`，按 `Edit` / `MultiEdit` / `Write` 的参数算，只算完成的那些）和它们返回的图片（`TRunImages`：约 36x22 的缩略图，右对齐到内容区右缘）。
  鼠标悬停（或键盘聚焦）在缩略图上弹出小预览，写着来自哪一步（这一步在摘要里的说法，跟着界面语言，如「Read a file」「读取了文件」，即词典 `tools` 与 `tasks` 里的词条）与第几张（「2 of 2」）；点击（回车、空格）打开大图查看器（`TImageViewer`），左右方向键切换这一行的图片，Escape 关闭；
  触屏上点一下直接打开查看器。图片是存好的附件，经渲染上下文的 `resolveAttachmentUrl` 读（`TChatApp` 从 `useChatThreads` 拿到）。子 agent 步骤里的图片在它自己的视图里，不在主对话的摘要行上。
- **展开之后**：子 agent 是一行（`TSubAgentStep`：状态、任务说明、类型、工具调用次数、用时，进行中时转圈且用时每秒走），点它打开它自己的视图；后台命令是一行（`TBackgroundCommandStep`：它自己的状态与用时，展开是命令行与退出说明）。
- **子 agent 的视图（`TSubAgentPanel`）**：标题是任务说明；状态、类型、模型、是否后台、工具调用次数、用时；它收到的完整提示（长的收成几行，「Show more」展开）；它此刻在做什么；
  然后按顺序是它写的字、它的工具调用（同样的摘要行，带图片；每一步按自己的结果显示完成或失败，只有还没返回的那一步算进行中，子 agent 接下来在做什么看上面那句）、它再派出的子 agent（一行，点开进入，有返回）和最后交回的报告。子 agent 再派出的子 agent，CLI 不发它的步骤，视图里写明这一点，只有状态、用量与报告。
  在跑时有「Stop」按钮。
- **侧栏列表（`TBackgroundTasks`）**：「Background tasks」一组，每个进行中的子 agent 或后台命令一行：左边图标（子 agent / 命令）、名称、右边已用时间（「18s」「6m」，每秒走）和一个停止方块；
  完成的收成一行「15 finished」，点开逐个列出（结局与用时）。点一个子 agent 打开它的视图，每次点击都发 `select`。
- **放在哪里**：`TChatApp` 的 `subAgentAside` 说侧栏的哪个页签显示子 agent 的视图：打开一个子 agent 时侧栏打开到这个页签，关掉视图时侧栏回到之前的样子（之前关着就关上，之前在别的页签就回去）；
  用户关掉侧栏或换了页签，视图也随之关闭。没给 `subAgentAside`（或没有 `#aside`）时，子 agent 那一行在原地展开它的视图。

  ```vue
  <TChatApp v-bind="..." sub-agent-aside="sub-agent" aside-default-tab="tasks">
    <template #aside="{ tab }">
      <TSubAgentPanel v-if="tab === 'sub-agent'" :messages="chat.messages.value" />
      <TBackgroundTasks v-else :messages="chat.messages.value" />
    </template>
  </TChatApp>
  ```

  不用 `TChatApp` 的外壳自己 `provideSubAgentView(createSubAgentView({ panel, stopTask }))`，再把 `TSubAgentPanel` 放在想放的地方（它读同一个视图状态）。
- **停下一个任务**：`useChatThreads().stopTask(taskId)` 请服务端停下一个子 agent 或后台命令，这一轮照常（`POST /api/chat/threads/{threadId}/tasks/{taskId}/stop`，`@tnzi/core` 的 `useChatApi().stopTask`）。
  `TChatApp` 默认从屏幕上那个 `useChatThreads` 拿到它（与审批卡片同一个挑法），`stopTask` 属性可以换掉或传 `null` 去掉停止按钮。停下请求被接受后按钮显示「Stopping...」，直到任务以 `stopped` 结束；
  服务端拒绝（没有在跑的运行 409、CLI 做不到 501：ACP 的 agent 只能停整轮）经 `onError` 报（`operation` 为 `stop-task`）。
- **后台任务跨轮**：派出它的那一轮先结束、它还在跑时，它后来的进展与结局更新的是那一轮回答里的那条调用，不是正在写的下一条回答；等后台任务的那一行（`status: 'waiting'`）不会因此变成进行中。
  下一条消息接过了运行的进程、结局落在另一次运行里时，那条回答里带一个占位条目（同一个 id、没有工具名），加载对话时并回原处（`mergeTaskCalls`）。运行结束时还没做完的任务记为「已停止」。
- **数据形状**：起任务的那条工具调用带 `task`（`ToolTaskDetailDto`：`kind` `agent` / `command`、`taskId`、`description`、`status`、`startedAt`、`durationMs`、`toolUseCount`、`totalTokens`、`activity`、`result`，
  子 agent 还有 `agentType`、`model`、`prompt`、`steps`）；子 agent 调的工具是同一份 `toolCalls` 里 `parentId` 指向派出调用 id 的条目，没有 `textOffset`。
  **自己渲染 `toolCalls` 的应用**要先用 `replyToolCalls(toolCalls)` 去掉 `parentId` 属于本条回答里某个派出调用的条目，否则子 agent 的调用会出现在回答里。
- **无渲染的部分**（`@tnzi/ui-ai/headless`）：`replyToolCalls`、`isSubAgentCall`、`isBackgroundCommand`、`isListedTask`、`subAgentSteps`、`taskStatus`、`taskTitle`、`taskToolCount`、`taskElapsedMs`、`formatTaskDuration`、
  `collectTasks`、`findToolCall`、`findToolCallOwner`、`mergeToolCall` / `mergeTaskDetail` / `upsertToolCalls` / `mergeTaskCalls`、`useNow`；组件旁边（`@tnzi/ui-ai/components`）：`createSubAgentView`、`provideSubAgentView`、
  `useSubAgentView`、`taskStepDescription` / `taskStepState`、`runImages`、`runLineChanges` / `callLineChanges` / `lineDiff`，以及文件与命令工具的说法 `describeReadFile`、`describeEditFile`、`describeWriteFile`、`describeCommand`、`describeCodeSearch`。
  `ToolStepDescription` 多了 `ongoing` / `manyOngoing`：一步在它那一行结束后还在继续（后台任务）时的说法。
- **文案**：词典 `tasks.*` 与 `tools` 里文件与命令工具的说法（en / zh-cn）。自己写了 `AiLocaleMessages` 类型词典的，要补上。
- **图标**：`lucide:bot`、`lucide:terminal`、`lucide:check`、`lucide:x`、`lucide:square`、`lucide:chevron-right`、`lucide:arrow-left`，都在框架的图标清单里。

服务端怎样把子 agent 的帧归到派出它的调用、怎样停下一个任务，见 `docs/modules/ai-cli.md` §6.22。

## 对话框：`TDialog`

本包的通用对话框：标题、标准关闭按钮、在对话框里滚动的正文、可选的操作区。设置弹窗、命令面板、内联小组件的放大视图
与它用**同一套外壳**（遮罩、关闭按钮、标题间距、滚动条），所以一个产品里的所有对话框长得一样。

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { TDialog, TButton } from '@tnzi/ui-ai/components'

const open = ref(false)
const saving = ref(false)
const name = ref('')
</script>

<template>
  <TDialog v-model="open" title="Rename chat" size="sm" :busy="saving" @closed="name = ''">
    <input v-model="name" class="t-input" aria-label="Name" />
    <template #footer="{ close }">
      <TButton @click="close">Cancel</TButton>
      <TButton variant="primary" :loading="saving">Save</TButton>
    </template>
  </TDialog>
</template>
```

| Prop | 默认 | 说明 |
|---|---|---|
| `modelValue` | — | 开关（`v-model`） |
| `title` | — | 标题，也是对话框的可访问名称 |
| `description` | — | 标题下一行短说明。**少用**：多数对话框标题已经说够了 |
| `ariaLabel` | — | 没有 `title` 时的可访问名称（例如自定义 `#header` 里没有标题） |
| `role` | `'dialog'` | 打断用户的确认用 `'alertdialog'` |
| `size` | `'md'` | 宽屏宽度：`sm` 400 / `md` 520 / `lg` 720 / `xl` 960（令牌 `--tnzi-ai-dialog-width-*`） |
| `width` / `height` | — | 自定的宽 / 固定高（数字 = px，字符串原样用），永远不超过窗口；不给 `height` 时随内容长高，最高 `--tnzi-ai-dialog-max-height` |
| `fullscreen` | `false` | 任何宽度都占满窗口（顶部留白以下） |
| `flush` | `false` | 正文不留内边距：预览、图片、自己画行的列表 |
| `closable` | `true` | 显示关闭按钮 |
| `closeOnEscape` / `closeOnBackdrop` | `true` | Escape / 点遮罩是否关闭 |
| `busy` | `false` | 保存进行中：Escape、遮罩都不关，关闭按钮禁用，`aria-busy`；`modelValue` 设为 false 仍然会关 |
| `closeOnBack` | `true` | 窄屏布局（底部弹层、全屏）下，手机的返回键 / 返回手势像 Escape 一样关它，而不是离开页面；`closable` 或 `closeOnEscape` 为 false、`busy`、`beforeClose` 拒绝时不关，页面也不走。开关状态放在地址里、返回本来就能关它的对话框传 `false`（见「返回键关浮层」） |
| `narrowBreakpoint` / `narrow` | `768` / — | 窄于断点（或 `narrow`）时用窄屏布局，与 `TSettingsDialog`、侧栏抽屉同一个断点 |
| `narrowLayout` | `'sheet'` | 窄屏布局：底部弹起的面板，或 `'fullscreen'` |
| `initialFocus` | — | 打开时聚焦的元素（返回它），替代默认规则 |

插槽：默认（正文）、`#footer`（操作，靠右排；**主操作放最后**）、`#header`（替换标题块，关闭按钮仍在，除非 `closable` 为 false；
插槽参数 `titleId`，放到你自己的标题元素上作为 `id`，对话框就以它命名）。三个插槽都拿到 `close()`。
事件：`update:modelValue`；`closed` 在对话框关上、**离场动画结束后**发（适合在这里重置表单，不会在淡出时闪一下空表单）。
`close()` 也经 `ref` 暴露。`class`、`data-*` 等属性落在面板（`.t-dialog`）上。

行为：

- **焦点**：打开时落在带 `autofocus`（或 `data-autofocus`）的元素上，否则第一个可以打字的输入框（文件、勾选、单选、滑块、颜色这类输入不算；
  `display: none`、`hidden`、`inert` 里的也不算），否则对话框面板本身，**从不落在关闭按钮上**
  （那样会在对话框一出现时就亮起焦点环，按 Enter 还会把它关掉）。焦点环只在键盘操作时出现（`:focus-visible`）。
  Tab 困在对话框内，关闭后焦点回到打开它的元素。
- **层叠**：后打开的永远在上面（`--tnzi-ai-z-dialog` 加上它在「当前打开的对话框」里的深度），在对话框里打开的确认框盖在它上面，
  一次 Escape 只关最上面那个；从对话框里打开的菜单在对话框之上（见「层级」）。
  它与设置弹窗、命令面板、`@tnzi/ui` 的 `TConfirm` 共用同一个层栈（见下一节）。
- **点遮罩**：按下与松开都在遮罩上才算；在输入框里拖选文字、松手落在面板外不会关掉对话框。
- 打开期间页面不滚动；正文在对话框内滚动（本包的细滚动条），标题与操作区不动，内容滚到它们下面时才出现分隔线。
- 宽屏在顶部留白（`--tnzi-overlay-inset-top`）以下居中；窄屏面板按设备安全区留白；`prefers-reduced-motion` 时没有动画。
- **屏幕键盘**：底部面板与全屏（窄屏两种布局，以及 `fullscreen`）只占用户看得见的那部分窗口（视觉视口）。键盘弹出时标题与操作区留在键盘上方，
  正文在剩下的空间里滚动；这块空间变化时，对话框里获得焦点的输入框会被滚回可见处。键盘收起、页面在键盘下滚动都会跟着更新，键盘收起后恢复原样。
  遮罩仍盖满整个窗口；键盘盖住的部分不再额外留底部安全区。宽屏居中的对话框、以及没有 `visualViewport` 的浏览器，布局与以前一样。
  做法是对话框自己的外层元素按视觉视口留出上下内边距（`--tnzi-ai-viewport-inset-top` / `-bottom`），不改动 `document` 上的任何东西，
  所以叠开几个对话框、关掉之后都不留痕迹。iOS 以外的浏览器若键盘弹出时本身就缩小布局视口（例如 Android Chrome 设了
  `interactive-widget=resizes-content`），这两个值就是 0，行为与以前相同。

**给自己的特殊浮层用同一套外壳**：外壳是全局类（`dialog.css`，随 `@tnzi/ui-ai/style.css` 一起来），尺寸是 `--tnzi-ai-dialog-*` 令牌
（`applyAiTheme({ dialogPadding: '20px' })` 这类键也有）：

| 类 | 是什么 |
|---|---|
| `.t-dialog-backdrop` | 固定定位、盖满视口并变暗；上内边距就是顶部留白。面板怎么摆（居中、贴底）由你的元素决定 |
| `.t-dialog` | 面板：底色、边框、圆角、阴影、纵向 flex；`.t-dialog--narrow` 换成窄屏内边距与 40px 关闭按钮 |
| `.t-dialog__header` / `.t-dialog__heading` / `.t-dialog__title` / `.t-dialog__description` | 标题行；关闭按钮离上、右边缘 `--tnzi-ai-dialog-close-inset` |
| `.t-dialog__close` | 关闭按钮（无边框，悬停底色，键盘焦点环） |
| `.t-dialog__body` | 滚动的正文（再加上 `t-ai-scroll`）；`--flush` 无内边距 |
| `.t-dialog__footer` | 操作区，靠右；`is-divided`（头部同理）画分隔线 |

焦点、层栈、滚动锁仍然要接：`useFocusTrap`（`@tnzi/ui`）与 `useBodyScrollLock`（`@tnzi/ui-ai/headless`）。
要让自己的浮层也躲开屏幕键盘，用 `useVisualViewportInsets(el, enabled)`（`@tnzi/ui-ai/headless`）：它把元素在视觉视口上方、下方被挡住的高度写成
`--tnzi-ai-viewport-inset-top` / `--tnzi-ai-viewport-inset-bottom`，元素拿它们当上下内边距即可（`var(--tnzi-ai-viewport-inset-bottom, 0px)`）。
它只写这个元素，不碰页面；整个外壳都钉在可见区域上的应用外壳用的是另一个 `usePinnedViewport`。

**从自己手写的对话框换过来**，常见的对应关系：

| 原来 | `TDialog` |
|---|---|
| `v-model:open` / `:open` + `@update:open` | `v-model` / `:model-value` + `@update:model-value` |
| `title` / `description` | 同名 |
| `:width="560"`（px） | `:width="560"` 原样可用；能取整就换成 `size`（`sm` 400 / `md` 520 / `lg` 720 / `xl` 960） |
| `busy`（挡住关闭、禁用关闭按钮） | `busy`，语义相同 |
| 默认插槽 / `#footer="{ close }"` | 同名、同参数 |
| 关闭时立刻发的 `closed` | `closed` 在离场动画结束后发；要「一关就」的时机，监听 `update:modelValue` 的 false |
| 自己找第一个输入框聚焦 | 默认就这样；要别的元素就 `initialFocus` 或给它 `autofocus` / `data-autofocus` |
| 正文写死的内边距、被预览类内容用负 margin 抵消 | `flush` |
| `:deep()` 改对话框内部类 | 改用 `--tnzi-ai-dialog-*` 令牌；对话框自己的类名是 `.t-dialog*` |

**从一个元素里打开、关回到它里面**（`origin`）：对话框从被点的那个元素（卡片、缩略图）长出来——位置、大小、倾斜角——关闭时缩回去；
面板的内容在快到位时才淡入、收回时先淡出，所以缩放从不落在文字上，遮罩同步变暗。

```vue
<TDialog
  v-model="open"
  :title="note.title"
  :origin="() => cardEl"
  :before-close="confirmDiscard"
  @closed="reset"
>
  …
</TDialog>
```

| Prop / 事件 / 暴露 | 说明 |
|---|---|
| `origin` | 元素；`{ element, rotate }`（自己给倾斜角）；一个框 `{ left, top, width, height, rotate? }`（未变换的布局框，旋转绕中心，单位度）；或一个返回其中之一的函数。**打开和关闭时各问一次**，所以关闭前刚保存出来的新卡片也找得到。元素的中心取自它的包围盒、尺寸取自布局尺寸，倾斜角没给时从它计算后的 `transform` 读出。元素已不在（被删、`display:none`、滚出视口）时退回对话框自己的淡入上浮 |
| `transition` | `'default'`（默认）或 `'none'`：**只关掉内置的进出场动画**，遮罩、层叠、焦点陷阱、Escape、手机底部面板照旧。配合暴露的 `panel` 自己做动画 |
| `beforeClose(reason)` | 对话框**自己发起**的每一种关闭之前都会调用：`'escape'` / `'backdrop'` / `'close-button'` / `'back'`（窄屏上的返回键） / `'programmatic'`（插槽与暴露出来的 `close()`）。可以是 async；返回 `false` 就不关；抛错也不关，`close()` 返回的 promise 带着这个错误 reject。等待期间再次请求关闭会并入同一次 |
| `@opened` | 进场动画结束后 |
| `@closed` | 离场动画结束、对话框移除之后（每一种关闭都等动画跑完） |
| 暴露 `close(): Promise<boolean>` | 走 `beforeClose`，结果是是否真的关了；插槽拿到的 `close` 是同一个 |
| 暴露 `panel` | 面板元素（`.t-dialog`），对话框显示时有值 |

- **减弱动态效果**（`prefers-reduced-motion: reduce`）时只做淡入淡出，不缩放不位移。
- **父组件直接把 `modelValue` 设为 false** 时离场动画照样会跑完再移除，但不会调用 `beforeClose`、也不能取消：状态是父组件的，它已经决定了。
  要能取消，就让关闭走对话框自己（按钮用插槽的 `close`，代码里用暴露的 `close()`）。
- 动画用 Web Animations API，不支持的环境里对话框直接出现、直接消失。

**从自己写的「从卡片长出来」换过来**：给对话框的 `id` 与按 `.t-dialog-host` / `.t-dialog-host__panel` / 父级 `:has()` 关掉内置动画的全局 CSS
全部删掉；自己在 `open` 变化时做的 FLIP 删掉，改成 `:origin`（若原来传的是「中心 + 尺寸 + 倾斜」，换算成 `left = cx - width / 2`、
`top = cy - height / 2`，或直接传卡片元素）；拦截取消 / 保存 / 删除 / Escape / 关闭按钮再先播动画的那层包装删掉，「关之前要先做的事」
放进 `beforeClose`，按钮里调用插槽的 `close()`。

内联小组件（`TWidgetCall`）的放大视图现在就是一个 `TDialog`（`width` 1200、`height` 900、`flush`）：原来的 `.t-widget-overlay` 与
`.t-widget-overlay__*` 类已不存在，按它们写的样式覆盖要改到 `.t-dialog*` 上。

## 弹层：右键菜单、详情弹出层、停靠小窗

三个不挡住页面的浮层。前两个在 `@tnzi/ui-ai/controls`（背后只有 `vue` 与 `@iconify/vue`），停靠小窗在 `@tnzi/ui-ai/components`（它用 `@tnzi/ui` 的焦点陷阱）。

**`TContextMenu`：在鼠标位置打开的菜单**（右键菜单）。放在哪里都行，打开前不画任何东西；在元素的 `contextmenu` 事件里调 `openAt`：

```vue
<li @contextmenu="menu?.openAt($event)">{{ file.name }}</li>
<TContextMenu ref="menu" :label="file.name">
  <template #default="{ close }">
    <TMenuItem icon="lucide:pencil" @click="rename(); close()">重命名</TMenuItem>
    <TMenuSeparator />
    <TMenuItem icon="lucide:trash-2" danger @click="remove(); close()">删除</TMenuItem>
  </template>
</TContextMenu>
```

- `openAt(event)` 阻止浏览器自己的菜单，菜单左上角落在指针处，下方或右侧放不下时向上或向左打开；也可以传 `{ x, y }`（视口坐标）。
  从键盘打开（菜单键、Shift+F10）时事件没有指针位置，菜单开在当前焦点元素的左下方。
- 键盘同 `TDropdownMenu`：打开后焦点在第一项（触屏不移焦点），↑ / ↓ / Home / End 移动，Escape 关闭且不再往外传、焦点回到打开前的元素，Tab 关闭并移走焦点。
  在菜单外按下（左键右键都算）、页面滚动、窗口失去焦点都会关闭它。`label`、`minWidth`、`maxWidth`；暴露 `openAt`、`close`，事件 `update:open`。
- 定位来自 `TPopoverMenu` 的新属性 `at`（`{ x, y }`）：给了它，菜单不再对齐祖先盒子，而是对齐这个点。

**`TPopover`：不是菜单的弹出层**（合并状态的详情、一段说明、一个小表单）。`TPopoverMenu` 带 `role="menu"`，里面只该放菜单项；放内容用它：

```vue
<TPopover label="合并状态">
  <template #trigger="{ toggle, attrs }">
    <TButton variant="ghost" v-bind="attrs" @click="toggle">检查已通过</TButton>
  </template>
  <MergeDetails />
</TPopover>
```

| Prop / 插槽 | 说明 |
|---|---|
| `label` / `labelledBy` | 面板的无障碍名称，或内容里一个元素的 id |
| `side` / `align` | 开在触发器哪一侧（默认 `bottom`）、怎么对齐（`start` 默认 / `center` / `end`）；放不下时翻到另一侧，滚动与缩放时跟着走 |
| `width` | 面板宽度（数字 = px）；默认随内容，最宽 360px |
| `gap` | 与触发器的间距，默认 6 |
| `#trigger` | 插槽参数 `open`、`toggle()`、`attrs`（`aria-haspopup="dialog"`、`aria-expanded`、`aria-controls`），绑到触发按钮上 |
| 默认插槽 | 内容，插槽参数 `close()` |

- 面板是非模态对话框（`role="dialog"`，没有 `aria-modal`），在菜单层，`position: fixed`，所以对话框正文、滚动列表都裁不到它。`class` 等属性落在面板上。
- 它**留在 DOM 里触发器的后面**：Tab 顺序是触发器 → 面板里的控件 → 页面后面的内容。
- 打开时焦点进入面板（带 `autofocus` / `data-autofocus` 的元素，否则面板本身，读屏会读出它，Tab 进入第一个控件）；Escape 关闭、不再往外传、焦点回到触发器；
  Tab 离开面板、在外面按下、插槽的 `close()` 都会关闭；手机上返回键关闭。暴露 `open()`、`close()`、`place()`。
- 定位用 `placeBeside`（`@tnzi/ui-ai/headless`），它的 `align` 新增 `end`（与锚点的右边或下边对齐）。

**`TDockedDialog`：停靠在角落、不挡住页面的小窗**（正在写的邮件、便签、客服对话）。

```vue
<TDockedDialog v-model="composing" v-model:minimized="folded" title="新邮件" :before-close="saveDraft">
  <TInlineField label="收件人">…</TInlineField>
  <textarea class="t-input t-input--bare" />
  <template #footer="{ close }"><TButton variant="primary" @click="send().then(close)">发送</TButton></template>
</TDockedDialog>
```

| Prop / 事件 | 说明 |
|---|---|
| `modelValue` | 开关（`v-model`） |
| `minimized`（`v-model:minimized`） | 收起到只剩标题行（300px 宽）；正文保持挂载，已输入的内容都在；点标题行或它的展开按钮恢复 |
| `maximized`（`v-model:maximized`） | 放大：水平居中，最宽 960px，高度占满窗口（顶部留白以下） |
| `title` / `ariaLabel` | 标题与无障碍名称 |
| `corner` | `bottom-right`（默认）/ `bottom-left` |
| `width` / `height` | 停靠时的尺寸，默认 520 × 560，不超过窗口 |
| `resizable` | 显示收起与放大按钮，默认 true |
| `closable` / `closeOnEscape` / `closeOnBack` | 关闭按钮（默认有）；Escape 关闭（**默认关**：正在写的窗口不是一个顺手关掉的浮层）；手机上返回键关闭（默认开） |
| `flush` | 正文不留内边距 |
| `narrowBreakpoint` / `narrow` | 窄于 768（或 `narrow`）时占满屏幕（顶部留白以下）、没有放大按钮，收起时是贴底的一条 |
| `beforeClose(reason)` | 同 `TDialog`：`'escape'` / `'close-button'` / `'back'` / `'programmatic'`，返回 `false` 不关，可以 async（存草稿、确认丢弃） |
| `@closed` | 离场动画结束后 |
| 插槽 | 默认（正文）、`#footer`，都拿到 `close()`；暴露 `close()`、`panel` |

- **非模态**：`role="dialog"`、没有 `aria-modal`，没有遮罩，不困住焦点，页面照常滚动和操作。它在对话框层本身（`--tnzi-ai-z-dialog`），模态对话框、菜单、提示都在它上面。
- **焦点**：打开时进入小窗（规则同 `TDialog`：`autofocus` / 第一个输入框 / 小窗本身）；从收起恢复时同样；焦点在里面时收起，焦点落到展开按钮上；
  焦点在里面时关闭，焦点回到打开前的元素。收起按钮带 `aria-expanded`，放大按钮带 `aria-pressed`，三个按钮有提示与无障碍名称（词典 `common.minimize` / `maximize` / `restore` / `close`）。
- **手机上**它占满屏幕，这时按模态层处理：焦点困在里面、页面不滚动、Escape 只在 `closeOnEscape` 时关、返回键经 `beforeClose` 关闭。
- 外壳是 `dialog.css` 的 `.t-dialog` / `.t-dialog__body` / `.t-dialog__footer`，类名 `.t-docked-dialog`，状态在 `data-state`（`docked` / `minimized` / `maximized` / `full`）。

## 表单控件的焦点：一套样式

本包画的每一个表单控件，获得焦点时都是同一个样子：**边框是「当文字读」的强调色**（`--tnzi-ai-accent-text`，
浅色品牌色在白底上会被挪到读得清的明度），**外面一圈 3px 的浅强调色光环**（`--tnzi-ai-accent-soft`）。品牌色换成什么、明暗模式是哪个都成立；
原始品牌色做边框的话，黄色品牌在白底上对比度约 1.3:1，聚焦了看起来也像没聚焦。

| 令牌 | 默认 | 说明 |
|---|---|---|
| `--tnzi-ai-focus-border` | `var(--tnzi-ai-accent-text)` | 聚焦时的边框色 |
| `--tnzi-ai-focus-ring-width` | `3px` | 光环宽度 |
| `--tnzi-ai-focus-ring-color` | `var(--tnzi-ai-accent-soft)` | 光环颜色 |
| `--tnzi-ai-focus-ring` | `0 0 0 <width> <color>` | 完整的 `box-shadow` 值 |
| `--tnzi-ai-focus-border-error` / `--tnzi-ai-focus-ring-error` | 危险色 / 浅危险色光环 | 出错的字段聚焦，形状相同 |
| `--tnzi-ai-focus-border-warning` / `--tnzi-ai-focus-ring-warning` | 警告色 / 浅警告色光环 | 带警告的字段 |
| `--tnzi-ai-control-border-hover` | `var(--tnzi-ai-text-tertiary)` | 鼠标悬停时字段的边框（比静止时深一档，不是品牌色） |

**覆盖范围**：
- `@tnzi/ui-ai/controls` 的控件与 `.t-input` / `.t-select` / `.t-check`。
- 本包自己的原生字段：登录页字段、对话输入框（`TThreadComposer`、`TPromptInput`、访客欢迎页）、会话搜索、标题重命名、消息编辑、
  设置弹窗搜索、外观管理、Recents 搜索、页面工具栏搜索。
- 门禁 `__tests__/conventions/focus-style.test.ts`：本包里画边框色或阴影的 `:focus` / `:focus-within` 规则必须取这些令牌。

**让自己的控件一致**：

```css
.my-input:focus {
  outline: none;
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}
.my-input[aria-invalid='true']:focus {
  border-color: var(--tnzi-ai-focus-border-error);
  box-shadow: var(--tnzi-ai-focus-ring-error);
}
```

要改样式就改令牌，**在 `:root` 上改**：`--tnzi-ai-focus-ring` 这类组合值在声明处就解析了，只在某个子树里改 `--tnzi-ai-focus-ring-color`
不会影响已经组合好的光环。对应的 `applyAiTheme` 键：`focusBorder`、`focusRingWidth`、`focusRingColor`、`focusRing`、`focusBorderError`、
`focusRingError`、`focusBorderWarning`、`focusRingWarning`、`controlBorderHover`。

**按钮、行、标签页、选项的键盘焦点**（`:focus-visible`）是另一种样子：一条 2px 的线，`--tnzi-ai-focus-outline`（默认
`2px solid var(--tnzi-ai-accent-text)`，即「当文字读」的强调色）。本包里所有键盘焦点描边都取它（门禁同上一个文件），
浅色品牌色（黄色）在浅色表面上也看得见；原来用的是原始强调色，黄色时约 1.3:1。自己的控件写 `outline: var(--tnzi-ai-focus-outline)`，
用 ui-ai 搭起来的应用里没写的元素也会得到这条线（见「应用标记：整页规则」）。`applyAiTheme` 键：`focusOutline`。

## 控件：`@tnzi/ui-ai/controls`

应用的页面与对话框用的一套控件。两档尺寸：**30px（`sm`，默认）是应用内页面与对话框的唯一尺寸**，按钮、输入框、下拉同高，
放在一行里对齐；**36px（`md`）只给访客落地的独立页面**（分享链接、邀请、授权确认）的主操作。颜色全部取 `--tnzi-ai-*`，跟随主题与明暗；
强调色由使用方的主题决定。

```ts
import { TButton, TSelect, TSwitch, TDropdownMenu, TMenuItem } from '@tnzi/ui-ai/controls'
// 同样从 '@tnzi/ui-ai/components' 导出
```

对话框 `TDialog`、确认与图片查看器 `TImageViewer` 在旁边的入口 `@tnzi/ui-ai/dialogs`（样式同在 `controls.css`，见「只用控件」）。

`@tnzi/ui-ai/controls` 背后的导入图里只有 `vue` 与 `@iconify/vue`，外加本包自己的依赖 `uqr`（`TQrCode` 按需加载）（门禁 `__tests__/conventions/controls-entry.test.ts` 沿导入图检查，动态导入也算，构建出的 `dist/controls.js` 再查一遍），
样式在包样式表 `style.css` 里，也有一份只含控件的样式表 `controls.css`：只用控件的页面（桌面窗口、小工具页）见下文「只用控件」。

| 组件 | 用途 | 要点 |
|---|---|---|
| `TButton` | 按钮 | `variant`：`primary`（强调色填充，一个界面只留给它的主操作）/ `soft`（浅强调色底、强调色字：一页有多张卡片、每张都有主操作时用它，不用一排黄色主按钮）/ `secondary`（默认，描边）/ `ghost`（工具栏、行内）/ `danger`（不可撤销）/ `link`（长得像链接、行为是按钮，没有框、行高随文字，悬停下划线：「查看全部提交」）；`size`：`sm` 30px / `md` 36px；`icon`；`loading`（保持宽度、显示转圈、不接受点击、`aria-busy`）；`block`；`type`；`inverse`（`ghost` / `link` 放在反色深底上：取所在条的文字色 `currentColor`，悬停是这个颜色的浅底，而不是变成正文色） |
| `TIconButton` | 只有图标的方形按钮 | `label` 既是无障碍名称也是 tooltip（默认在下方，跟随祖先的 `data-tooltip-side`，`tooltipSide` 可改）；`size`：`sm` 30px（默认，与 `TButton`、`.t-input` 同高，放在一排对齐）/ `xs` 28px（密集列表行、字段里）/ `md` 34px（需要更大目标的页头）；`active`（开着，带 `aria-pressed`；不传就是普通动作）；`danger`（悬停变红）；`inverse`（反色深底上，同 `TButton`） |
| `TField` | 带标签的字段 | 标签在上，控件在默认插槽，下面是错误（危险色，`role="alert"`）或提示。插槽给 `id`、`describedBy`、`invalid`，分别放到控件的 `id`、`aria-describedby`、`aria-invalid` 上；`required` 只画星号 |
| `TSelect` | 单选下拉 | 见下方键盘；`options: TSelectOption[]`（`value` / `label` / `disabled`）；`placeholder`；`size`；`bare`（没有自己的框，宽度随所选的字、箭头紧跟其后：写信窗的「发件人」行，放进 `TInlineField` 与 `.t-input--bare` 的行一排看起来一致；在行里由行显示焦点，单独用时有键盘焦点线）；`class` / `style` 落在外层盒子，其余属性落在字段按钮上（放进 `TField` 的插槽正合适）；选中后在字段上派发原生 `change` |
| `TSearchSelect` | 从很多项里挑一个（关联的记录：几百上千个客户里选一个） | 字段与 `TSelect` 一样，打开后列表上方是搜索框（焦点在里面）。两种来源：`options: TSearchSelectOption[]`（`value` / `label` / `disabled` / `description`：行尾较淡的第二段文字，区分同名项）全在手里，按输入过滤标签与 `description`（不分大小写、不分重音）；或 `load(query, { offset, signal })` 按输入取一页 `{ options, hasMore }`，停顿 `debounce`（默认 250ms）后才调用，新输入会 abort 上一个请求且丢弃它迟到的结果，滚到列表底部（或用键盘走过最后一行）取下一页。列表有加载中、无匹配、加载失败（带重试，重试的是失败的那一页）三种状态行。远程时选中的记录多半不在第一页，用 `selectedLabel` 给出它的名字；都没有时显示值本身。`clearable` 在字段里加一个清除按钮（设为 `null`）；`placeholder`、`searchPlaceholder`、`size`、`disabled`；发 `update:modelValue` 与 `select(option \| null)`；选中或清除后在字段上派发原生 `change`。`class` / `style` 落在外层盒子，其余属性落在字段按钮上。取数逻辑 `createSearchSelectSource` 也单独导出 |
| `TMultiSelect` | 多选下拉 | 已选项是字段里的标签（各带移除按钮），列表在字段下方原位展开（不上浮层，对话框的 Tab 循环与滚动照常），`searchFrom`（默认 7）项以上带搜索；不在选项里的已选值保留；所有文字由调用方给（`label`、`placeholder`、`searchPlaceholder`、`emptyText`、`removeLabel` 含 `{name}`）；值的每次变化都有一次冒泡的原生 `change`：勾选框的由勾选框自己派发，移除标签时在字段（`[data-multi-field]`）上派发；搜索框自己的 `change` 不往外传 |
| `TSwitch` | 开关 | `role="switch"` 的原生按钮，38×22，Space / Enter 按原生按钮切换；`loading` 时显示正在保存的状态、不接受点击；关闭时旋钮 `--tnzi-ai-switch-knob`，打开时强调色上的前景色 |
| `TTag` | 状态短词 | 22px 胶囊，`tone`：`neutral` / `accent` / `success` / `warning` / `danger` / `info`；`dot`；`size="sm"` 18px、11px 字，给密集列表。在一行里不被挤扁（`flex-shrink: 0`），让位的是旁边的名字 |
| `TSplitButton` | 一个操作加它的几种变体（「提交」+「提交并推送、撤销上次提交」） | 主体发 `click`，右侧箭头打开 `TDropdownMenu`，`#menu` 插槽放 `TMenuItem`（插槽参数 `close`），菜单与整个控件对齐；`menuLabel`（箭头的无障碍名称与提示，必填）；`variant`：`primary`（默认）/ `soft` / `secondary`；`size`、`icon`、`loading`（只锁主体）、`mainDisabled`（只禁主体，菜单照常可用：没有可提交的内容时「提交」不能点，「撤销上次提交」还能用；菜单项各自的 `disabled` 不受影响）、`disabled`（两部分都锁）、`type`、`align`、`placement`、`minWidth` / `maxWidth` |
| `TBadge` | 图标上的数字角标或圆点（「3 个会话在跑」、未读数） | `icon` 或默认插槽（图标、`TIconButton`、头像）；`count`（0 不显示，`showZero` 例外），超过 `max`（99）显示 `99+`；`dot`；`tone`：`accent`（默认）/ `neutral` / `warning` / `danger`；`label` 是读屏听到的整句：有 `icon` 时整个角标是 `role="img"` 的图，包着控件时是控件名称后面的补充文字；数字本身对读屏隐藏。角标外圈是表面色，换底色时在它上面设 `--t-badge-ring` |
| `TInlineField` | 信纸式的一行字段（写信窗的「收件人」「主题」） | 标签在行首（次要文字色），控件占满其余，行下一条细线是唯一的框，焦点在行内时细线变成焦点色；默认插槽给 `id`（标签的 `for` 指向它），控件用 `.t-input t-input--bare`；`#end` 放行尾的东西（「抄送」）；`labelWidth` 让一组行的标签同宽、字段对齐 |
| `TNotice` | 页面或对话框里的一行说明 | `tone`：`info` / `warning` / `error`（`role="alert"`）/ `success` |
| `TSpinner` | 等待 | 圆环，`size`：`md` 24px（默认，页面或面板）/ `sm` 16px（一行文字、按钮里）；在一行里不被挤扁；装饰（`aria-hidden`），外层元素负责说明状态 |
| `TProgress` | 用了多少、做到哪了（用量、配额） | 细条：`value` 是百分比，夹在 0..100（超过上限画满，不溢出）；`tone`：`accent`（默认）/ `warning`（快到上限）/ `danger`（到了）；`height`（默认 6px）、`width`（默认随容器）；给了 `label` 才是带 `aria-valuenow` 的 `progressbar`，不给是装饰（旁边的数字已经说了同样的话） |
| `TQrCode` | 一个值画成可扫的二维码（验证器的设置链接、一次性登录链接） | `value`、`label`（无障碍名称，`role="img"`；值本身不朗读，它常是密钥或长链接）、`size`（默认 152px）、`ecc`（默认 `M`）、`margin`（留白格数，默认 4）。一条 SVG：白底黑码，深色主题下也一样（反色的码很多扫码器读不了），留白画在图里而不靠 CSS 内边距；编码器 `uqr` 第一次画时才 `import()`，之前是同样大小的白框，布局不跳 |
| `TDropdownMenu` | 触发按钮 + 小菜单 | 搭在 `TPopoverMenu` 上；`#trigger` 插槽给 `open`、`toggle()`、`attrs`（`aria-haspopup` / `aria-expanded`），默认插槽给 `close()`；`align`、`placement`、`minWidth`、`maxWidth`、`label` |
| `TMenuItem` / `TMenuSeparator` | 菜单项与分组线 | 放在 `TMenuPanel` 里（任何 `TPopoverMenu`、`TDropdownMenu`、`TContextMenu` 画的都是它）：`icon`、`danger`、`disabled`、`value`（右侧只读值）、`shortcut`（右侧快捷键文字，较淡）、`expanded`（行尾箭头，`true` 时转向下并设 `aria-expanded`；不传就没有箭头）、`checked`（勾）。插槽 `#leading` 代替 `icon` 放进与 `icon` 同尺寸（15px）的图标框：应用自己的图片（`<img>` 填满）、带角标的图标（`<TBadge :icon :count>`）；`#trailing` 在行尾（快捷键之后、箭头与勾之前）再放东西。行高、悬停、按键仍由框架管，页面不必再写 `.t-popover-menu__item` 这类类名；菜单项的样式在面板里，脱离面板单独放是没有样式的 |
| `TMenuPanel` | 不浮动的菜单：页面本身就是菜单（托盘菜单窗口、一列操作） | 弹出菜单画的就是这块面板，`TMenuItem` 在里面与弹出菜单逐项一致（同一份样式）；`role="menu"`，上下键、Home、End 在可用项之间移动（`keyboard`，默认开）；`windowKeys`：焦点不在任何元素上时（整个窗口就是菜单、打开时没有焦点）也接住这几个键，第一下下键到首项、上键到末项，焦点在页面别处的输入框或控件里时不管；Escape、Tab 一直留给页面；`label`（顶部标题行）；`flat`（去掉边框与圆角，铺满一个以自身为边框的窗口）。没有阴影与定位，也不监听外部点击 |
| `TImagePicker` | 图片本身就是更换它的控件（头像、项目图片） | 默认插槽是页面自己的图（没有图时也有东西可显示），`round` 为圆形；点击或回车 / 空格打开文件选择，可以把文件拖上去（拖入时描边），发 `pick(file)`（同一个文件再选一次也算）；`has` 为真时角上有移除按钮（`removeLabel`，悬停 / 聚焦时出现，触屏一直在），发 `remove`；`busy` 时转圈、不接受点击、不显示移除；`accept`、`changeLabel`（无障碍名称）、`changeHint`（`title`）。读取、裁剪、上传归页面。图上的遮罩与图标取 `--tnzi-ai-image-veil` / `--tnzi-ai-on-image` |
| `TSegmented` | 分段选择（`components/layout`，也从 controls 导出） | 选项新增 `badge`（大于 0 才显示的数字，警告色）、`badgeLabel`（读屏听到的话）、`dot`（没有数字可给的提醒点：`true` 警告色、`'danger'` 危险色，比如某个邮箱同步出错；有 `badge` 时不显示）、`dotLabel`（读屏听到的话）、`running`（那边有东西正在进行：一个转动的细环，在点的位置；同时有 `dot` 时环绕着点转，「要看一眼」与「还在动」一起读出；`prefers-reduced-motion` 下不转）、`runningLabel`（读屏听到的话，默认词典的 `common.running`「Working」）、`attrs`（绑在选项按钮上，先于组件自己的属性，改不掉 `role`）。图标选项的点与环在图标右上角，且 `badgeLabel` / `dotLabel` / `runningLabel` 接在选项名称后面成为按钮的可访问名称（「Code, Working」） |
| `TSettingGroup` / `TSettingRow` | 设置面板的分组与一行（`components/layout`，也从 controls 导出） | `TSettingGroup`：`title`（或 `#title`）、`separator`（组上方的分隔线，第一组自动不画）；`TSettingRow`：`label`、`description`（或同名插槽）、默认插槽放控件（贴右）、`stacked`（宽控件放到文字下面）；放不下时控件换到标签下面 |

**`TDataGrid`：成行的数据**（查询结果、CSV、导出前的预览），排成数据客户端那样：每行一条 24px 的紧凑行、隔行底色、格线，
行号与表头在滚动时固定（行号在左、表头在上），数字靠右，缺失值（`null` / `undefined`）斜体浅色、与空字符串分开，长值截成一行。

```vue
<TDataGrid
  :columns="columns"
  :rows="result.rows"
  label="查询结果"
  :first-row="page * pageSize + 1"
  selectable
  v-model:active-cell="chosen"
>
  <template #empty>没有行</template>
</TDataGrid>
```

| Prop / 模型 / 插槽 | 说明 |
|---|---|
| `columns: DataGridColumn[]` | `name`（表头）、`hint`（表头提示，如列的类型；默认是列名）、`kind`（`number` 靠右、按数值排；`text` 默认，按读的顺序排）、`muted`（次要文字色：二进制、没人读的 id）、`sortable: false`、`compare(a, b)`（自己的排序）、`format(value)`（显示成什么，默认 `String(value)`） |
| `rows` | 每行一个数组，每列一个值 |
| `label` | 表格的无障碍名称（必填） |
| `rowNumbers` / `firstRow` | 行号列（默认有）与第一行的号（翻页后从后面开始） |
| `zebra` / `mono` | 隔行底色、等宽字，默认都开 |
| `nullText` | 缺失值显示成什么，默认 `NULL` |
| `sortMode` | `local`（默认，这里排）/ `remote`（只报告排序，由服务端排） |
| `v-model:sort` | `{ column, direction }` 或 `null`；点表头按升序、降序、原序循环，表头带 `aria-sort` |
| `v-model:widths` | 每列宽度（px）或 `null`；第一次调宽时按当前布局把所有列固定下来，之后表格按这些宽度截断 |
| `selectable` / `v-model:active-cell` | 单元格可聚焦、可选中（`{ row, column }`，`row` 是在 `rows` 里的下标，不受排序影响）；选中后展示什么（完整值、复制）由页面画 |
| `cellClass(cell, value)` | 给某个单元格加类 |
| `locale` | 文字排序用的语言，默认页面的 |
| `#cell` | 单元格内容，参数 `value`、`text`（默认显示的字）、`row`、`column`、`columnDef` |
| `#empty` | 没有行时表头下显示的内容 |

- 换一组列（按列名）时排序、宽度、选中都清空；同一组列的下一页保留。
- 所有行都渲染，大结果请分页。根元素就是滚动容器，`class` 等属性落在它上面（放进 flex 布局时给它 `flex: 1`）。
- 颜色只取 `--tnzi-ai-*`：表头与行号是 4% 文字色混进表面色，隔行 2.5%，格线 `--tnzi-ai-border`，选中格浅强调色底加强调色内框。
- 纯函数 `nextDataGridSort`、`sortDataGridRows` 与常量 `DATA_GRID_MIN_COLUMN_WIDTH`（48）、`DATA_GRID_KEY_STEP`（16）同时导出。

**原生元素的类**（`controls.css`，全局）：`.t-input`（`<input>` / `<textarea>`）、`.t-select`（下拉字段的外观，`TSelect` 的按钮与少数保留的原生 `<select>`）、
`.t-check`（`<label class="t-check"><input type="checkbox"> 文字</label>`）、`.t-mono`（等宽）、`.t-input--bare`（没有自己的框：给 `TInlineField` 的行和自己画框的条用，聚焦时也不画光环，焦点由外面的行表示）、`.t-input--code`（一次性码：邮件里的确认码、验证器的六位数字。16px 等宽、数字等宽、字距拉开，要醒目好输；与 `--md` 一起用：`class="t-input t-input--md t-input--code"`，36px 高。`TApprovalCard` 的确认码输入就是它）。默认 30px，加 `--md`（`.t-input--md`、`.t-select--md`）为 36px；
`aria-invalid="true"` 画成错误；聚焦取焦点令牌（边框 + 光环，见「表单控件的焦点」）。窄于 640px 时字号 16px，iOS 聚焦时不再放大页面。
下拉箭头用 `currentColor` 画（按钮字段是遮罩，原生 `<select>` 是两道渐变），明暗都跟着字色走。

**键盘**：

| 组件 | 键 |
|---|---|
| `TSelect` | 关着：↓ / ↑ / Enter / Space 打开，直接打字打开并跳到第一个匹配项。开着：焦点留在字段上，当前项是 `aria-activedescendant`；↑ / ↓ 移动（跳过禁用项，首尾循环），Home / End 到两端，打字跳到下一个以它开头的选项（停顿 0.5 秒重新开始），Enter / Space 选中，Escape 关闭列表且不再往外传（外面的对话框不会跟着关），Tab 关闭并移走焦点。输入法组字中的按键不处理 |
| `TSearchSelect` | 关着：↓ / ↑ / Enter / Space 打开，直接打字打开并把这个字放进搜索框。开着：焦点在搜索框（`role="combobox"`，当前项是 `aria-activedescendant`；菜单定位完成的下一帧才移入，这之前打的字也进搜索框）；↑ / ↓ 移动（跳过禁用项，到底时取下一页），Enter 选中，Escape 关闭列表、焦点回到字段、不再往外传（外面的对话框不会跟着关），Tab 关闭并移走焦点。输入法组字中的按键不处理 |
| `TMultiSelect` | 切换按钮打开 / 关闭；打开时搜索框获得焦点；Tab 在搜索框与勾选框之间移动，Space 勾选；Escape 关闭列表、焦点回到切换按钮、不再往外传；在外面按下也会关闭 |
| `TDropdownMenu` | 打开后焦点在第一项（触屏不移焦点）；↑ / ↓ 在可用项间移动并循环，Home / End 到两端；Escape 关闭、焦点回到触发按钮、不再往外传；Tab 关闭并移走焦点。焦点等菜单定位完成的下一帧才移入：定位前菜单是 `visibility: hidden`，浏览器不会聚焦看不见的元素 |
| `TSegmented` | ← / → / ↑ / ↓ 移动选择并循环（单选组），Tab 只停在选中项 |
| `TDataGrid` | 表头是按钮，Enter / Space 排序；列边是 `role="separator"`，聚焦后 ← / → 调宽 16px（Shift 64px），Home 到最窄。`selectable` 时是 ARIA grid，只有一个 Tab 停点：← / → / ↑ / ↓ 在格间移动，Home / End 到行首行尾（加 Ctrl 到整表首尾），Page Up / Down 跳 10 行，Enter / Space 选中或取消，Escape 取消选中（有选中时才拦下，不再往外传） |
| `TSplitButton` | 主体与箭头是 Tab 顺序里的两个按钮；箭头带 `aria-haspopup` / `aria-expanded`，Enter / Space / ↓ 打开菜单，之后同 `TDropdownMenu`（Escape 回到箭头） |
| `TSwitch`、`TButton`、`TIconButton` | 原生按钮：Space / Enter 按下 |

**提示气泡（`vTooltip`）**：默认在控件**下方**居中（原为右侧），放不下时翻到另一侧；侧栏 rail 里的按钮显式用 `side: 'right'`
（`TRailButton` 默认如此）。外观是反色小气泡：背景 `--tnzi-ai-tooltip-bg`（文字色），字 `--tnzi-ai-tooltip-fg`（画布色），12px，内边距 4px 8px，
没有阴影与位移动画，超过 280px 换行。悬停 350ms 后出现、刚关掉另一个时立即出现、键盘聚焦立即出现。

**触屏上**没有悬停，提示改为**长按出现**：手指按住不动 500ms（`TOOLTIP_LONG_PRESS_MS`）后出现，抬起后再停 1.5 秒（`TOOLTIP_TOUCH_SHOW_MS`），
和手机上长按工具栏图标看名字一样；这次长按**不会**同时触发控件（随后的点击和系统的长按菜单都被拦下）。轻点照常执行、不出提示；
按住时手指移动超过 10px 算滚动，不出提示。控件自己的长按另有用途（拖动、长按菜单）时传 `{ text, touch: 'none' }` 关掉。

长按没人会去找，所以提示只适合放**控件的名字**（名字本来就在无障碍名称里，触屏用户靠图标认得）。**触屏上也必须看得到的补充说明**
（「排序只作用于本页的行」）不要放进提示，放进 `TInfoTip`：一个「i」小按钮，点一下（鼠标、手指、Enter / Space 都一样）在旁边打开那段说明，
是一个非模态的 `TPopover`，Escape 关闭、焦点回到按钮，在外面按下或 Tab 离开也关闭。

```vue
<span>排序 <TInfoTip text="排序只作用于本页显示的行。" /></span>
```

`TInfoTip`：`text` 或默认插槽，`label`（按钮与面板的名称，默认词典的 `common.moreInfo`「More information / 更多说明」），`icon`（默认 `lucide:info`），
`side`（默认 `top`）、`align`（默认 `center`）、`width`（默认随文字，最宽 280px）、`size`（`xs` 默认 / `sm`）。

**一片区域的提示统一朝一个方向**：在那片区域的容器上写 `data-tooltip-side`（`top` / `right` / `bottom` / `left`），里面没有自己指定 `side`
的提示（`v-tooltip="'文字'"`、`TIconButton`、`TSegmented` 的图标选项……）都朝那边，就近的那个属性生效；自己写了 `side` 的照旧。
优先级：控件自己的 `side` > 最近祖先的 `data-tooltip-side` > 下方。

```vue
<aside class="inspector" data-tooltip-side="left">
  <TIconButton icon="lucide:pin" label="固定" />   <!-- 朝左 -->
</aside>
```

用 DOM 属性而不是 provide / inject：指令挂在普通元素上也能读到，不需要组件上下文；代价是传送到 `<body>` 的内容（菜单、对话框）
不在那片区域的 DOM 里，不受它影响。常量 `TOOLTIP_SIDE_ATTRIBUTE`、函数 `tooltipSide(el, own?)`（`@tnzi/ui-ai/headless`，属性常量也在 `controls`）。

**按容器宽度排版：`useElementSize`**（`@tnzi/ui-ai/headless` 与 `@tnzi/ui-ai/controls`）：

```ts
const grid = ref<HTMLElement | null>(null)
const { width } = useElementSize(grid)
const columns = computed(() => (width.value >= 900 ? 3 : width.value >= 560 ? 2 : 1))
```

参数是元素的 ref 或 getter，元素换了就改观察新的，为 `null` 时是 0 × 0；`{ box: 'border-box' }` 量含内边距与边框的盒子（默认量内容盒）。
值是浏览器报的 CSS 像素（可能带小数）。调用它的作用域结束（组件卸载）时自动停止，也可以调返回的 `stop()`。没有 `ResizeObserver` 的环境只在元素出现时量一次。

### 只用控件：桌面窗口、小工具页

设置面板、托盘菜单、快问这类小窗口要启动快，不该带上对话、markdown、代码高亮、工作流，也不该加载整份 `style.css`。
它们只用 `@tnzi/ui-ai/controls` 与样式表 `@tnzi/ui-ai/controls.css`，样子与完整应用里的同一套控件一致（同一份令牌、同一批组件样式）。

**安装**

```bash
pnpm add @tnzi/ui-ai vue @iconify/vue
```

`@tnzi/core`、`@tnzi/ui` 是本包的必需 peer（`@tnzi/ui-ai` 根入口、`chat`、`auth`、`plugin` 等都要它们），npm 7 以上与 pnpm（`auto-install-peers` 默认开）
会自动装上；只用控件的页面不会导入它们，打包产物里没有它们的代码（门禁 `controls-entry.test.ts` 检查源码与构建出的 `dist/controls.js`）。关掉了自动装 peer 的包管理器（yarn、`auto-install-peers=false`）
会提示缺少这两个 peer，可以忽略，或者一并装上。

**导入**

```ts
// main.ts
import { createApp } from 'vue'
import { applyControlsTheme } from '@tnzi/ui-ai/controls'
import '@tnzi/ui-ai/controls.css'

applyControlsTheme({ mode: 'auto', accent: '#0d9488' })
createApp(App).mount('#app')
```

```ts
// vite.config.ts：link: 安装（直接链到框架工作树）时让 vue 只有一份；从本包导入，不用另外依赖 @tnzi/ui
import { tnziSingletons } from '@tnzi/ui-ai/vite'

export default defineConfig({ plugins: [vue(), tnziSingletons()] })
```

```vue
<script setup lang="ts">
import { TSettingGroup, TSettingRow, TSwitch, TSelect, TButton, TNotice } from '@tnzi/ui-ai/controls'
</script>
```

`@tnzi/ui-ai/controls.css` 包含：浅色与深色的全部 `--tnzi-ai-*` 令牌、强调色读取的五个 `--tnzi-*` 变量的默认值（teal）、上文「原生元素的类」那些全局类
（`.t-input`、`.t-select`、`.t-check`、`.sr-only` 等）、提示气泡、细滚动条、对话框的共用外框（`dialog.css`：遮罩、面板、标题、关闭按钮、正文、底栏），
以及 controls 与 dialogs 两个入口用到的每个组件的样式：控件打开的浮层（`TPopoverMenu` / `TDropdownMenu` / `TContextMenu` 菜单、`TPopover`、
`TToastHost` 提示消息、`TSelect` / `TSearchSelect` 的列表）与 `TDialog`、`TConfirmHost`、`TImageViewer`。不含重置样式（reset），
也不含对话、侧栏等其他域的任何规则（门禁 `__tests__/conventions/controls-stylesheet.test.ts`：组件样式的 `data-v-*` 与主构建一致、不出现入口之外组件的类、
控件模板里写的每个类都有样式；体积预算在 `.size-limit.mjs`）。它与 `style.css` 二选一，不要同时加载。

页面自己给正文的基础样式（按钮、字段、菜单这些控件自己定字号与行高；设置行的文字和页面上的其他文字跟随页面）。实测加上这几行后，与加载整份 `style.css` 的同一页逐像素一致：

```css
html { line-height: 1.5; }
body {
  margin: 0;
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-body);
  font-size: 14px;
}
```

**对话框：`@tnzi/ui-ai/dialogs`**

`TDialog`、确认（`useConfirm()` 与显示它的那一个 `TConfirmHost`，`createConfirmer`）、图片查看器 `TImageViewer`（类型 `ViewerImage`）及其类型从这个入口导入，样式已在 `controls.css` 里：

```ts
import { TDialog, TConfirmHost, TImageViewer, useConfirm } from '@tnzi/ui-ai/dialogs'
```

例如一张表里行的图片，点一下看大图（缩放、平移、键盘与关闭都是查看器自己的，见「图片查看器」）：

```vue
<script setup lang="ts">
import { computed, ref } from 'vue'
import { TImageViewer, type ViewerImage } from '@tnzi/ui-ai/dialogs'

const props = defineProps<{ rows: { id: string; name: string; imageUrl: string }[] }>()
const images = computed<ViewerImage[]>(() => props.rows.map((row) => ({ src: row.imageUrl, alt: row.name })))
const viewing = ref<number | null>(null)
</script>

<template>
  <button v-for="(row, i) in rows" :key="row.id" type="button" @click="viewing = i">
    <img :src="row.imageUrl" :alt="row.name" />
  </button>
  <TImageViewer v-model:index="viewing" :images="images" />
</template>
```

查看器的几个词（上一张、下一张、放大、缩小、适应屏幕、关闭、没能加载）与控件一样经注入键取应用的词典（`imageViewer` 与 `common.close`），这个入口不带整本英文词典；
旧词典里缺的词回落到英文。它画的图标（关闭、左右、放大、缩小）都在 `CONTROLS_ICON_NAMES` 里，离线打包见下文「图标」。

它不并进 controls 入口：对话框是页面浮层栈的一层（Escape 与 Tab 交给最上面那一个打开的浮层，不管是哪个包打开的），那个栈是
`@tnzi/ui/headless` 的 `useFocusTrap` 加 `@tnzi/core` 的页面级登记表。所以导入这个入口会带上这两个子路径（都是本包的 peer，门禁
`controls-entry.test.ts` 限定它只能走到 `@tnzi/ui/headless`、`@tnzi/core/headless`、`@tnzi/core/utils`）；另做一个只属于控件的栈，在同时有两者的页面上
一次 Escape 会被回答两次。同样的组件也从 `@tnzi/ui-ai/components` 导出。

**语言：`createAiI18nPlugin`**

控件与对话框自己说的几个词（加载中、重试、关闭、取消、确认、搜索、无匹配……）和其他组件的文案都取应用提供的词典。
`createAiI18n(messages)` 要在某个根组件的 `setup` 里调用；没有这样一个根组件的应用（每个 `defineCustomElement` 自定义元素都是一个单独的 Vue 应用）
在应用上安装插件：

```ts
import { defineCustomElement } from 'vue'
import { createAiI18nPlugin } from '@tnzi/ui-ai/i18n'
import { en, zhCn } from '@tnzi/ui-ai/locales'

const aiI18n = createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: navigator.language })

customElements.define('tz-table', defineCustomElement(TzTable, { configureApp: (app) => app.use(aiI18n) }))
customElements.define('tz-form', defineCustomElement(TzForm, { configureApp: (app) => app.use(aiI18n) }))

// 用户换语言时：装了这个插件的每个应用一起换
aiI18n.setLocale('zh-CN')
```

| 选项 / 成员 | 说明 |
|---|---|
| `messages` | 按语言标签的词典（默认只有 `en`） |
| `locale` | 起始语言（BCP 47），默认 `fallbackLocale` |
| `fallbackLocale` | 没有匹配词典时用的那一份，默认 `en` |
| `locale`（返回值） / `setLocale(tag)` | 当前语言（按请求的原样，`zh-Hans-CN` 仍是它）；切换。词典按标签本身、去掉末尾子标签（`zh-Hans-CN` → `zh-Hans` → `zh`）、同一语言的任一份（`zh-TW` 用 `zh-CN`）依次匹配（`matchAiLocale`），不分大小写，`_` 当 `-` |
| `messages`（返回值） / `setMessages(tag, catalogue)` | 当前生效的词典；之后再加载的语言包 |

插件同时提供语言标签本身（`useAiLocale()` 读它），内联小部件的 `hostContext.locale` 就取这个（见「导出子路径」的 `widget`）。
用 `createAiI18n` 的应用可以传 `createAiI18n(messages, { locale })`（字符串、ref 或 getter）达到同样效果。

**明暗与强调色：`applyControlsTheme`**

没有 `@tnzi/ui` 的主题上下文时用它。它不导入任何包（属于 controls 入口），在 `<html>`（或传入的元素）上：

| 选项 | 作用 |
|---|---|
| `mode: 'light' \| 'dark'` | 去掉或加上 `dark` 类，并设 `color-scheme` |
| `mode: 'auto'` | 跟随系统的 `prefers-color-scheme`，直到同一元素下一次设模式 |
| `accent: '#rrggbb'` | 以内联样式写 `--tnzi-primary`、`--tnzi-primary-rgb`、`--tnzi-on-primary`、`--tnzi-primary-text-on-light`、`--tnzi-primary-text-on-dark` |
| `accent: null` | 去掉上次写的强调色，回到样式表的默认值 |

没给的选项不动。强调色只收十六进制（`#0d9488` / `#fd2`），否则抛错且什么都不改。填充上的文字与当作文字用的强调色按 `@tnzi/ui` 主题上下文的同一套规则算
（填充上白字低于 3:1 换成近黑墨色；强调色文字在浅色、深色表面上都至少 4.5:1），浅黄这类强调色也读得清
（测试逐个对照主题上下文的结果）。`controlsAccentVars(accent)` 只算不写，`CONTROLS_ACCENT_VARS` 是这五个变量名。
有主题上下文的应用（`defineChatApp`、`createTnziUi`）不要再调它：两边写同一组变量，后写的生效。

`applyAiTheme`（`@tnzi/ui-ai/theme`）是覆盖单个 `--tnzi-ai-*` 令牌用的，函数本身不依赖 `@tnzi/ui`，但 `@tnzi/ui-ai/theme` 这个入口还导出主题快照
（要 `@tnzi/ui`），只用控件的页面换明暗与强调色用 `applyControlsTheme` 即可。

**图标：离线打包**

控件经 `@iconify/vue` 画图标。桌面窗口里不该向 Iconify 公共 API 取图（断网时是空白方块，联网时把用户在看什么告诉第三方）：
在构建时从图标集里挑出用到的那些，运行时注册，再关掉远程取图。控件与 dialogs 入口的组件自己会画的图标是 `CONTROLS_ICON_NAMES`
（选择框的箭头、提示条的标志、提示消息的关闭按钮、图片查看器的缩放按钮……，门禁 `__tests__/conventions/controls-icons.test.ts` 让它与两个入口的源码一致）；
页面通过 `icon` 属性、菜单项传进去的图标是页面自己的。

```ts
// scripts/icons.mjs：构建前运行，产出 src/icons.json
import { writeFileSync } from 'node:fs'
import { getIcons } from '@iconify/utils'
import { icons as lucide } from '@iconify-json/lucide'
import { CONTROLS_ICON_NAMES } from '@tnzi/ui-ai/controls'

const own = ['lucide:sun', 'lucide:moon', 'lucide:bell'] // 页面自己用的
const names = [...CONTROLS_ICON_NAMES, ...own].map((name) => name.slice('lucide:'.length))
writeFileSync('src/icons.json', JSON.stringify(getIcons(lucide, names)))
```

```ts
// main.ts，挂载之前
import { addCollection } from '@iconify/vue'
import { disableRemoteIcons } from '@tnzi/ui-ai/controls'
import icons from './icons.json'

addCollection(icons)
disableRemoteIcons() // 之后没注册过的名字渲染为空白，不再发请求
```

`disableRemoteIcons` 从 `@tnzi/ui-ai/controls` 导入：`@tnzi/ui-ai/icons` 导出的是同一个函数，但那个入口会带上 `@iconify/utils` 与整张图标名单。

控件的图标目前都在 `lucide` 一个集合里；页面也用别的集合时按前缀分组，每个集合各 `getIcons` 一次。只在构建时导入 `@iconify-json/*`：
运行时静态导入整个集合会把它整个打进包里（lucide 约 620 kB）。`@iconify/utils` 与 `@iconify-json/*` 由页面自己装（`pnpm add -D`）。

**实测**（Vite 生产构建，一页用了按钮、开关、分段、下拉、多选、设置组与设置行、提示条、菜单、提示消息、空页）：
JS 169.85 kB（gzip 59.32 kB），其中 `vue`、`@iconify/vue` 与 27 个图标约 86 kB（gzip 32.5 kB）；CSS 53.88 kB（gzip 9.69 kB），
同一页加载整份 `style.css` 时 CSS 是 308.91 kB（gzip 47.60 kB）。两种加载方式截图逐像素一致（浅色与深色）。

## 状态与反馈：列表三态、空页、确认、提示

列表除了内容之外的三种状态、一张还没有东西的页面、不可撤销操作前的确认、操作后的短提示，各有一个组件，所以每个页面说话的方式一样。
前四个与提示在 `@tnzi/ui-ai/controls`（背后只有 `vue` 与 `@iconify/vue`），确认在 `@tnzi/ui-ai/dialogs` 与 `@tnzi/ui-ai/components`（它搭在 `TDialog` 上）。
它们自己说的几个词（加载中、重试、关闭、取消、确认）取应用 `createAiI18n` / `createAiI18nPlugin` 提供的词典，没有提供时是英文。

**`TStateView`：列表的加载、出错、空**

```vue
<TStateView
  :loading="loading"
  :error="errorText"
  :empty="!loading && items.length === 0"
  empty-art="stopwatch"
  :empty-title="t('tasks.empty')"
  @retry="load"
>
  <template #action><TButton icon="lucide:plus" @click="create">新建任务</TButton></template>
  <template #empty-more>
    <div class="t-templates-divider" />
    <ul class="t-templates">
      <li v-for="item in templates" :key="item.id">
        <TTemplateRow :icon="item.icon" :name="item.name" :description="item.description" :when="item.when" @pick="start(item)" />
      </li>
    </ul>
  </template>
  <TaskList :items="items" />
</TStateView>
```

| Prop / 插槽 / 事件 | 说明 |
|---|---|
| `loading` | 占位行（`rows`，默认 3；64px 高，`compact` 时 44px），`aria-busy`，读屏读「加载中」。手机上 180ms 内结束的加载不出现占位 |
| `error` | 出错的话（已经是人话）：云朵图标、最多四行、`role="alert"`；**监听了 `retry` 才有「重试」按钮** |
| `empty` + `emptyArt`（或 `#empty-art`） | 内容页的空状态，即 `TEmptyState`：插画、`emptyTitle`、`emptyText`，`#action` 是填满它的操作，`#empty-more` 放在下面（模板） |
| `empty`，不给插画 | 面板、对话框、搜索无结果：`emptyIcon`（默认 `lucide:inbox`）、`emptyTitle`、`emptyText`、`#action` |
| `compact` | 在对话框或卡片里，而不是一整页：占位行更矮，留白更少 |
| 默认插槽 | 以上都不成立时显示的内容 |

优先级：`loading` > `error` > `empty` > 内容。

**`TEmptyState` 与 `TEmptyArt`：空页**

`TEmptyState`（`art`、`title`、`text`，插槽 `#art`、`#action`、默认）是每个内容页的空状态：一幅插画、一行灰字、一个描边按钮，下面可以再放别的。
`TEmptyArt` 有 13 幅同一笔法的手绘插画（`EMPTY_ART_NAMES`）：`stopwatch`（定时）、`history`（运行记录）、`blocks`（为你做的应用）、`apps`（已装应用）、
`store`（商店）、`project`（项目）、`device`（设备）、`folder`（文件）、`trash`（回收站）、`library`（资料库）、`note`（便签）、`bell`（提醒）、`mail`（邮件）。
页面要画的东西不在其中时，在 `#art` 插槽里放自己的 `<svg viewBox="0 0 96 96" class="t-empty-art" aria-hidden="true">`，要填色的形状加 `class="t-empty-art__fill"`：
线条是文字色、2.6 圆头描边，填色是 `--tnzi-ai-sidebar-selected` 并向左下错开一点，和内置的读起来是一套。

**`TTemplateRow`：空页上的模板**

`icon`、`name`、`description`，可选 `when`（一条事实，例如「每天 8:00」）与 `whenIcon`（默认 `lucide:clock`）；点它发 `pick`。
它是一个按钮：指向或键盘聚焦时图标变成「+」。`#peek` 插槽放页面叠在行上的东西（相对行定位），有它时文字给右侧留出 148px。
一组模板放进 `<ul class="t-templates">`（两列，窄于 640px 一列），上面一条 `<div class="t-templates-divider" />`（`--tnzi-ai-wave` 遮罩画的波浪线）。

**`useConfirm` 与 `TConfirmHost`：不可撤销操作前的确认**

```ts
import { useConfirm } from '@tnzi/ui-ai/components'

const confirm = useConfirm()
const deleted = await confirm({
  title: '删除这个项目？',
  message: '它的会话和文件都会删除，不能恢复。',
  confirmText: '删除',
  danger: true,
  checkbox: '同时删除它的数据库',
  action: (alsoDatabase) => api.deleteProject(id, { alsoDatabase }),
  describe: (error) => toMessage(error),
})
```

| 选项 | 说明 |
|---|---|
| `title`、`message` | 标题与正文（正文保留换行） |
| `confirmText` / `cancelText` | 两个按钮的字；不给时是词典里的「确认」「取消」 |
| `danger` | 确认按钮画成危险色 |
| `checkbox` | 一个额外勾选项，它的值交给 `action` |
| `action(checked)` | 确认后要做的事。**运行期间对话框保持打开并忙碌**（Escape、遮罩、两个按钮都不起作用，确认按钮转圈）；**出错时不关闭**，错误就地显示在对话框里（`TNotice`，`role="alert"`），可以再试或取消；成功后关闭 |
| `describe(error)` | 失败时显示的话；不给时用错误的 `message` |

结果：确认（且 `action` 成功）为 `true`，取消、Escape、点遮罩、关闭按钮为 `false`。还没回答时又来一个请求，前一个按 `false` 结束。
焦点与键盘是 `TDialog` 的：打开时焦点进入对话框（`role="alertdialog"`），Tab 困在里面，Escape 取消，关上后焦点回到打开它的元素。
`useConfirm()` 在组件外（store、请求助手）也能用。

**`useToast` 与 `TToastHost`：操作后的短提示**

```ts
import { useToast } from '@tnzi/ui-ai/controls'

const toast = useToast()
toast.success('已保存')
toast.error(`唤醒失败：${message}`)
```

`success` / `info` / `warning` / `error`（第二个参数 `{ duration, actions, busy }`，`duration` 毫秒，0 为不自动消失）都返回这条提示的**句柄**
（`ToastHandle`：`id`、`update(changes)`、`dismiss()`、`shown`）；`toast.dismiss(句柄或 id)` 收回一条，`clear()` 全部收回。

**最多两个操作**（「撤销」「立即发送」）：`actions: [{ label, run, keepOpen? }]`（超过两个抛 `RangeError`，`TOAST_MAX_ACTIONS`）。
每个操作是提示上的一个强调色文字按钮，按下执行 `run` 并收起这条提示（`run` 抛错时照样收起，错误交给应用）；`keepOpen: true` 的操作按下后提示留着，
由它自己去 `update`。带操作的提示默认停 6 秒（`TOAST_ACTION_DURATION_MS`，错误也是至少 6 秒），`duration` 照样可改。

**更新一条提示**：`update({ text, tone, actions, busy, duration, quiet })`。`busy` 时语气图标换成转圈、提示带 `aria-busy`；`duration` 从现在起重新计时（0 为一直留着；
指针或焦点在提示上时等它离开才开始）；`quiet: true` 的这次改字不播报（倒计时每秒一跳用它，读屏只听到开头和结果）。提示已经消失后 `update` 什么都不做。

撤销发送，读秒、两个按钮、发送中、发出：时钟和发送归页面，提示只显示走到了哪一步。

```ts
const toast = useToast()
let left = 5
const note = toast.info(`${left} 秒后发出`, {
  duration: 0,
  actions: [
    { label: '撤销', run: () => { clearInterval(tick); restoreDraft() } },
    { label: '立即发送', keepOpen: true, run: () => sendNow() },
  ],
})
const tick = setInterval(() => (--left > 0 ? note.update({ text: `${left} 秒后发出`, quiet: true }) : sendNow()), 1000)

async function sendNow() {
  clearInterval(tick)
  note.update({ text: '发送中…', busy: true, actions: [] })
  try {
    await send()
    note.update({ text: '已发出', tone: 'success', busy: false, duration: 3000 })
  } catch (error) {
    note.update({ text: `没有发出：${describe(error)}`, tone: 'error', busy: false, duration: 6000 })
  }
}
```

操作按钮可以 Tab 到，按下后焦点回到进入提示之前的元素（同关闭按钮）；`keepOpen` 的操作把自己的按钮更新掉时，焦点留在提示里，落到关闭按钮上。
指针或焦点在提示上时它不消失，读屏把提示的文字与按钮一起读到。提示不排队、不存历史，不是通知中心：要留下来的消息归页面自己。
本包自己画：窗口顶部居中、在顶部留白（`--tnzi-overlay-inset-top`）下面，`--tnzi-ai-z-toast` 层，表面色、细边框、带语气色图标。
**同时显示的条数**：宽屏最多 3 条，窄于 768px（与 `TChatApp` 的侧栏抽屉同一个断点）只显示 1 条，新的替换旧的，因为手机上两条叠起来就盖住了页头；
超出时最旧的先让位，窗口变窄时多出的那几条当即收回（`TOAST_MAX_VISIBLE_WIDE`、`TOAST_MAX_VISIBLE_NARROW`）。
`TToastHost` 的 `narrowBreakpoint`（默认 768）改断点，`narrow` 强制其中一种；`TChatApp` 挂的宿主用它侧栏抽屉的同一个信号（`isMobile` / `sidebarMobileBreakpoint`）。
普通提示 3 秒、**错误 6 秒**（`TOAST_DURATION_MS`、`TOAST_ERROR_DURATION_MS`）；指针停在上面或焦点在里面时不消失。
无障碍：整组是 `aria-live="polite"` 区域，错误是 `role="alert"`；**提示从不抢焦点**；关闭按钮可以 Tab 到，焦点在某条提示上时按 Escape 关掉它，
焦点回到进入提示之前所在的元素（点关闭按钮也一样）；焦点不在提示上时 Escape 与提示无关。

**宿主**：`TChatApp` 自动挂上 `TConfirmHost` 与 `TToastHost`。自己搭外壳的应用在根组件旁挂一次：

```vue
<template>
  <RouterView />
  <TConfirmHost />
  <TToastHost />
</template>
```

同一个确认器 / 提示器挂了两个宿主时，只有先挂上的那个显示（所以在 `TChatApp` 旁边再挂一个不会出现两份）。
要一套独立的（测试、另一个独立的界面）：`createConfirmer()` / `createToaster()`，把它传给宿主的 `confirmer` / `toaster` prop。

## 层级：一套 z-index

本包每个浮起来的东西都从同一套令牌取 z-index（`styles/index.css`），从低到高：

| 令牌 | 默认 | 给谁 |
|---|---|---|
| `--tnzi-ai-z-sticky` | 10 | 在自己那一栏里浮起的条与按钮（回到底部、面板头） |
| `--tnzi-ai-z-drawer` | 50 | 窄屏侧栏抽屉、嵌入式聊天面板；它们的遮罩低一级 |
| `--tnzi-ai-z-dialog` | 1000 | 模态对话框（`TDialog`，以及搭在它上面的 `TSettingsDialog`、`TConfirmHost`；`TCommandPalette`），每个再加上它在当前打开的对话框里的深度；非模态的 `TDockedDialog` 就在这一层本身，所以任何模态对话框都盖在它上面（手机上它占满屏幕时按模态对话框算深度） |
| `--tnzi-ai-z-popover` | 2000 | 菜单、选择器、弹出层（`TPopoverMenu`、`TContextMenu`、`TPopover`）：**在任何对话框之上**，所以从对话框里打开的菜单盖在对话框上 |
| `--tnzi-ai-z-toast` | 3000 | 短暂提示 |
| `--tnzi-ai-z-tooltip` | 3100 | 提示气泡 |

深度按「当前同时打开的对话框」计，关掉就还回去，所以对话框的值永远远低于菜单层。
消费方自己的浮层也取这些令牌（`z-index: var(--tnzi-ai-z-popover)`），就不会出现「菜单开在对话框下面」。
本包源码里不再出现 10 以上的 z-index 字面量（门禁 `__tests__/conventions/layering.test.ts`）。

**`TPopoverMenu` 不再被裁掉**：它照旧对齐「最近的定位祖先」，但现在是 `position: fixed`、在菜单层、由脚本定位（放不下就翻到另一侧，
不超出窗口，滚动与缩放时跟着走），所以放在对话框正文（`overflow: auto`）或任何滚动列表的底部也不会被切掉，并且盖在对话框上。
**它仍然留在你放它的 DOM 位置**：从祖先元素上找菜单项、在祖先上处理按键的写法照常工作。只有当某个祖先建立了比菜单要盖住的内容更低的
层叠上下文时，才需要 `teleport`（把菜单移到 `<body>`，此后菜单项不再在你的组件元素里）。原来写在组件上的 `class` 等属性落在菜单元素上。

## 内容页的工具栏：`TPageToolbar`

非对话的内容页（定时任务、笔记、文件、设备……）用同一条工具栏，所以每一页的搜索、筛选、排序与主操作长在同一个位置、同一个尺寸。
一行：左边是缩小范围的东西（`#start`：`TSegmented` 标签页，或者什么都没有）；右边依次是

1. 搜索（`search` + `v-model:query`）：一个图标，点开后原地变成紧凑的输入框（`TToolbarSearch`）。Escape 清空并收起、焦点回到图标；
   输入框为空时离开就收起；有内容则一直开着；
2. 页面自己的控件（`#actions` 插槽），如果有；
3. 图标操作（`actions`）：筛选、排序……有 `items` 的打开菜单（`checked` 标当前选择），`active` 表示已经应用了什么（按钮变色）；
4. 主操作（`primary`）：小号主按钮，默认带「+」图标；有 `items` 时整个按钮打开菜单，再加 `split` 则按钮本身执行、只有箭头打开菜单。

控件都是小号（`--tnzi-ai-toolbar-control-size`，30px）。菜单是 `TActionMenu`，在菜单层，盖在页面、粘性条与对话框之上。
**手机上**（窄于 `narrowBreakpoint`，默认 768）仍是一行：标签页横向滚动，选中的那个自动滚进视野（挂载时与切换时，只在滚动容器里横向滚，
页面本身不动）；**两个及以上**图标操作收进一个「更多」菜单（带菜单的操作变成子菜单，当前选择仍打勾，已应用的那一项右侧标「已应用」），
只有一个时保留它自己的按钮（收进去只会多点一下；不按剩余宽度测量）；主按钮只留图标，标签作为可访问名称；搜索打开时独占这一行。
用鼠标或手指打开的菜单不在第一项上画键盘焦点环，按了键才出现。

下面这段就是定时任务页那条工具栏：

```vue
<script setup lang="ts">
import { computed, ref } from 'vue'
import { TPageToolbar, TResourcePage, TSegmented, type PageToolbarAction } from '@tnzi/ui-ai/components'

const tab = ref<'yours' | 'runs' | 'templates'>('yours')
const query = ref('')
const stateFilter = ref<'all' | 'active' | 'paused'>('all')
const sortKey = ref<'next' | 'name'>('next')

const actions = computed<PageToolbarAction[]>(() => [
  ...(tab.value !== 'templates'
    ? [{
        id: 'filter',
        label: 'Filter',
        icon: 'lucide:list-filter',
        active: stateFilter.value !== 'all',
        items: (['all', 'active', 'paused'] as const).map((s) => ({ id: s, label: s, checked: stateFilter.value === s })),
      }]
    : []),
  ...(tab.value === 'yours'
    ? [{
        id: 'sort',
        label: 'Sort',
        icon: 'lucide:arrow-down-up',
        items: (['next', 'name'] as const).map((k) => ({ id: k, label: k, checked: sortKey.value === k })),
      }]
    : []),
])

const primary = {
  label: 'New task',
  items: [
    { id: 'compose', label: 'With the assistant', icon: 'lucide:message-square-plus' },
    { id: 'manual', label: 'Manually', icon: 'lucide:pencil-line' },
  ],
}

function onSelect(actionId: string, itemId: string) {
  if (actionId === 'filter') stateFilter.value = itemId as typeof stateFilter.value
  if (actionId === 'sort') sortKey.value = itemId as typeof sortKey.value
}
</script>

<template>
  <TResourcePage max-width="page">
    <template #toolbar>
      <TPageToolbar
        v-model:query="query"
        search
        search-placeholder="Search tasks"
        :actions="actions"
        :primary="primary"
        @select="onSelect"
        @primary-select="(id) => (id === 'compose' ? compose() : openForm(null))"
      >
        <template #start>
          <TSegmented
            v-model="tab"
            label="Views"
            :options="[
              { value: 'yours', label: 'Yours' },
              { value: 'runs', label: 'Runs' },
              { value: 'templates', label: 'Templates' },
            ]"
          />
        </template>
      </TPageToolbar>
    </template>

    <!-- the list -->
  </TResourcePage>
</template>
```

| Prop / 插槽 / 事件 | 说明 |
|---|---|
| `#start` | 左侧：标签页、筛选芯片。插槽参数 `narrow` |
| `search` / `v-model:query` | 显示可收起的搜索；`query` 是原样的输入，要不要防抖由页面决定 |
| `searchLabel` / `searchPlaceholder` / `searchWidth` / `searchMaxlength` | 可访问名称（默认词典里的「搜索」）、占位、宽屏下展开宽度（默认 200）、最大长度 |
| `searchInputAttrs` / `searchButtonAttrs` / `moreAttrs` | 页面自己的属性（`data-*` 测试钩子等），分别落在搜索输入框、打开搜索的图标、手机上的「更多」按钮上 |
| `actions: PageToolbarAction[]` | `{ id, label, icon, active?, disabled?, items?: ActionMenuItem[], attrs? }`，按顺序；`attrs` 落在按钮上，手机上落在它在「更多」菜单里的那一行；菜单项也有自己的 `attrs` |
| `#actions` | 页面自己的控件，放在图标操作之前、不参与手机上的收起；用 `.t-page-toolbar-icon` / `.t-page-toolbar-button` 类就和标准控件一样 |
| `primary: PageToolbarPrimary` | `{ label, icon?（默认 lucide:plus，null 不要图标）, items?, split?, disabled?, loading?, attrs?, menuAttrs? }`；`attrs` 落在主按钮上，`menuAttrs` 落在 split 的箭头上 |
| `#primary` | 整个替换主操作 |
| `narrowBreakpoint` / `narrow` | 手机布局的断点（默认 768）或强制 |
| `@action(id)` | 没有菜单的图标操作被点击 |
| `@select(actionId, itemId)` | 图标操作菜单里选了一项（手机上的「更多」菜单也走这里） |
| `@primary` / `@primary-select(itemId)` | 主按钮（split 时是它的主体）/ 主操作菜单里选了一项 |
| `@update:query` | 搜索输入 |

`TToolbarSearch`（`v-model`、`v-model:expanded`、`label`、`placeholder`、`width`、`maxlength`、`inputAttrs`、`buttonAttrs`；
有内容时显示本包自己的清除按钮，清空后保持展开、焦点留在输入框，浏览器原生的搜索「x」不再出现）与 `TSegmented`（`v-model`、`options`、`label`；
单选组，方向键移动选择）也可以单独用。

`TSegmented` 只有一种尺寸，与有没有文字无关：整组高度等于工具栏控件的高度 `--tnzi-ai-toolbar-control-size`（30px），
外层内边距 2px、圆角 9px，选项高 26px、圆角 7px、字号 13px、图标 16px，所以和旁边的搜索、图标按钮对齐。另有两种形态：

- `icon-only`：每个选项只显示图标（宽 32px），`label` 是它的可访问名称和提示（`v-tooltip`，在下方）。没有 `icon` 的选项仍显示文字。
- `block`：整组占满容器宽度，各选项均分；放不下的文字以省略号截断。

图标尺寸由 CSS 自定义属性 `--tnzi-ai-segmented-icon-size` 决定（默认 16px），设在 `TSegmented` 自己的 `class` 上或包住它的元素上都生效，
不需要 `:deep`。选项跟着图标变大：图标上下各至少留 4px（选项高取 26px 与「图标 + 8px」中较大者，整组再加上下 2px），
`icon-only` 的选项左右各至少留 7px（宽取 32px 与「图标 + 14px」中较大者），角上的提示点与运行环始终贴在图标的右上角，滑块按选项的实际尺寸放置。
图标 20px 时选项 34 × 28px、整组高 32px，比旁边 30px 的工具栏控件高 2px；不大于 18px 的图标不改变任何尺寸。

```vue
<!-- 侧栏头部的模式切换：20px 图标 -->
<TSegmented v-model="mode" label="Mode" icon-only class="mode-switch" :options="modes" />
<style scoped>
.mode-switch { --tnzi-ai-segmented-icon-size: 20px; }
</style>
```

选中项的底色是一块独立的滑块（`.t-segmented__thumb`，在选项之下、不响应点击、读屏不可见）：选择变化时它的位置和宽度一起过渡，从旧选项滑到新选项
（`--tnzi-ai-duration-base` + `--tnzi-ai-easing`）。首次渲染、选项尺寸变化（字体加载、容器变宽变窄、文字或角标变化）、选项列表变化时直接到位，
`prefers-reduced-motion` 下一律直接到位。它在组里与选项一起排布，所以组被横向滚动时跟着选项滚。颜色与阴影就是原来选中项的：亮色是表面色加浅阴影，
暗色是 `--tnzi-ai-selected` 加一圈描边；整组或选中项禁用时它同样半透明。选中的按钮本身背景透明，只把文字变成正文色。

超过 13em 的选项文字（用邮箱地址命名的邮箱）以省略号截断，一个长选项不会把其它选项挤出这一行；被截断的选项悬停或键盘聚焦时提示显示全文，
没截断的不显示提示。`block` 形态按均分的宽度截断，不受 13em 限制。

自己的控件要同样的「截断时才显示全文」：`v-tooltip="{ text, whenTruncated: '.my-label' }"`，选择器指向控件里可能被截断的那个元素，
它的文字放得下时不显示提示（判定 `isTruncated(element)` 同样从 `@tnzi/ui-ai/headless` 导出）。

```vue
<TSegmented v-model="layout" label="Layout" icon-only
  :options="[{ value: 'list', label: 'List', icon: 'lucide:list' }, { value: 'grid', label: 'Grid', icon: 'lucide:layout-grid' }]" />
<TSegmented v-model="period" label="Period" block
  :options="[{ value: 'day', label: 'Day' }, { value: 'week', label: 'Week' }, { value: 'month', label: 'Month' }]" />
```

手机上照旧不换行，在工具栏左侧横向滚动，选中的一项滚进视野。工具栏一行的最小高度也是 `--tnzi-ai-toolbar-control-size`。

**页面属性与测试钩子**：上面每一处 `attrs` 都先于工具栏自己的属性绑定，所以工具栏依赖的 `role`、`aria-haspopup`、`data-menu-item`
等不会被覆盖，`class` / `style` 会合并。**收进「更多」菜单的条目 id 是 `<actionId>::<itemId>`**（`FOLDED_ID_SEPARATOR`、`foldedItemId`），
可以直接写选择器：`[data-menu-item="filter::paused"]`。因此 action 的 `id` 里不能出现 `::`（会直接抛错），菜单项的 id 可以。
`@select(actionId, itemId)` 收到的始终是拆开后的原始 id。**页面宽度**：`TResourcePage` 的工具栏与正文共用一栏，`max-width="page"` 就是
`--tnzi-ai-page-max-width`（1040px），两侧留白是 `--tnzi-ai-page-padding-x`（32px，手机 `--tnzi-ai-page-padding-x-narrow` 16px）；
所有内容页都用 `page`，宽度就一致。正文底部留白是 `--tnzi-ai-page-padding-bottom`（32px），某一页底部压着浮动的东西（上传托盘）时传
`body-padding-bottom`（数字是 px）。

**卡片网格**：正文里的卡片放进 `.t-resource-grid`（`--wide` 每列更宽），每张 `TResourceCard` 都与所在行一样高，卡片外面包了一层
（链接、右键菜单的触发元素）时也一样，所以一张描述长的卡片不会让这一行参差不齐。手机上（≤ 640px）卡片更紧：内边距 10px、各行间距 6px。
手机上不想显示的状态标签由页面自己不传：`:tag="narrow ? '' : tag"`。

**从自己手搭的工具栏换过来**：

| 原来 | 换成 |
|---|---|
| 页面外层自己写的 `max-width: <页面宽度变量>; margin: 0 auto; padding: …` | `<TResourcePage max-width="page">`，删掉外层的宽度与左右留白；要别的宽度传数字 |
| `.xxx__toolbar`（`display:flex; justify-content:space-between; margin-bottom`） | `TResourcePage` 的 `#toolbar` 里放 `TPageToolbar`；间距由它们负责 |
| 左侧的分段标签（自己的 segmented 组件） | `#start` 里放 `TSegmented`（`v-model`、`options`、`label` 原样对应），或保留自己的组件 |
| `searchOpen` + `openSearch` / `closeSearch` / `onSearchBlur` + 图标按钮与输入框的切换 | `search` + `v-model:query`，这四样都删掉 |
| 一直显示的整宽搜索框 | 同上（变成可收起的）；要它一直展开就单独用 `TToolbarSearch` 并 `v-model:expanded` 固定为 true |
| 筛选图标 + 自己的菜单组件 + 菜单项（`checked`） + `:class="{ 'is-active': filterActive }"` | `actions` 里一项 `{ id: 'filter', icon, label, active: filterActive, items }`，在 `@select('filter', id)` 里改状态 |
| 排序图标 + 菜单 | `actions` 里一项 `{ id: 'sort', …, items }` |
| 只在某个标签页出现的操作（`v-if="tab !== 'templates'"`） | 在 `actions` 的 `computed` 里按条件放入 |
| 主按钮（primary 变体、`+` 图标）+ 箭头 + 菜单 | `primary: { label, items }`，`@primary-select(id)`；只是一个按钮就 `primary: { label }` + `@primary` |
| 菜单项里的分隔线 | 下一项加 `dividerBefore: true` |
| 菜单项的图标、危险样式 | `icon`、`danger` |
| 页面独有的控件（视图切换、刷新、批量操作） | 没有菜单的放 `actions`（`@action(id)`），否则放 `#actions` 插槽并用 `.t-page-toolbar-icon` / `.t-page-toolbar-button` 类 |

## 浮层：设置弹窗、层栈、顶部留白

**设置弹窗：`TSettingsDialog`**（也就是 `TChatApp` 的设置）。搭在 `TDialog` 上：900 × min(740px, 90vh)，正文无内边距，
遮罩、关闭按钮、焦点、Escape、层叠、返回键、`beforeClose` 都是 `TDialog` 的。对话框标题（`title`，默认词典里的「设置」）是它的可访问名称；
左栏 186px，分区按 `group` 分组，**组与组之间留空，没有组名标题**；分区项 32px 高、13.5px 字，图标在前，选中项有底色并带 `aria-current="page"`；
右侧是打开的分区：15px 的分区名（`<h3>`，内边距 16px 24px 4px），下面是这个分区的插槽，右栏自己滚动。

```vue
<TSettingsDialog
  v-model="open"
  v-model:active-section="section"
  :sections="[
    { id: 'account', label: '账户', icon: 'lucide:circle-user-round', group: 1 },
    { id: 'security', label: '安全', icon: 'lucide:shield-check', group: 1, badge: 2, badgeLabel: '2 项需要处理' },
    { id: 'appearance', label: '外观', icon: 'lucide:palette', group: 2 },
  ]"
  :before-close="confirmDiscard"
>
  <template #lead><TNotice>你的组织管理着这个账户的部分设置。</TNotice></template>
  <template #account>…</template>
  <template #security>…</template>
  <template #appearance>…</template>
</TSettingsDialog>
```

| Prop / 插槽 | 说明 |
|---|---|
| `sections` | `{ id, label, icon?, group?, badge?, badgeLabel? }[]`。`group`（字符串或数字）只决定哪些分区放在一起，按第一次出现的顺序排；`badge` 大于 0 才显示（警告色小圆角数字，靠右），`badgeLabel` 是读屏听到的话 |
| `activeSection` | 打开的分区（`v-model:active-section`）；打开时传了就进那一分区，不传则停在上次的分区 |
| `title` | 对话框标题与可访问名称 |
| `heading` | 分区名的覆盖；不给时是打开分区的 `label` |
| `#lead` | 分区名上方的内容（例如一条与所有分区有关的提示） |
| `beforeClose(reason)` | 关闭前询问（Escape、遮罩、关闭按钮、返回键），返回 `false` 不关；可以是 async（例如先问「放弃未保存的修改？」） |
| `showAccount` / `accountName` / `accountSubtitle` / `accountAvatar` / `switchable` | 可选：左栏顶上的账号（28px 头像、13.5px 名字、12px 副标题），`switchable` 时右侧有切换按钮（`switch-account` 事件） |
| `searchable` | 可选：左栏顶上 30px 的筛选框，按名称筛选分区，没有匹配时显示「没有匹配的设置。」 |
| `#rail-header` / `#rail-footer` | 替换账号与筛选框 / 放在分区列表下面 |
| `narrowBreakpoint` / `narrow` | 窄于断点（默认 768）或 `narrow` 时是手机布局 |
| `closeOnBack` | 同 `TDialog` |
| `flush` | 与 `TDialog` 的 `flush` 同义：分区内容不加内边距、占满分区名下方的剩余高度（`flex: 1`、`min-height: 0`，纵向 flex），右栏不再整体滚动；`#lead` 与分区名留在顶部（见下） |
| `#<分区 id>` | 分区内容，插槽参数 `{ section }` |
| 默认插槽 | 所有分区的内容放在一个插槽里，插槽参数 `{ activeSection, section }`；给了它就不再渲染按分区命名的插槽（见下） |

**分区内容的两种写法**：

- **按分区命名的插槽**（`#account`、`#security`…）：每个分区各自独立，打开时挂载、切到别的分区就卸载释放。
  切回来是一个新的面板，没保存的输入不会留下。适合每个分区自己加载、自己保存的设置页，也省内存。
- **一个默认插槽**：所有分区的内容都在里面，这个插槽在切换分区时一直挂着，不随分区重建。
  适合两种情况：要跨分区保留没保存的内容（在一个分区里输入一半，去另一个分区保存了别的设置，回来时还在）；
  或者面板要自己决定什么时候挂载、什么时候释放。通常是每个面板 `v-show`，打开过才挂（`v-if` 打开过 + `v-show` 当前）：

```vue
<TSettingsDialog v-model="open" v-model:active-section="section" :sections="sections">
  <template #default="{ activeSection }">
    <GeneralPane v-if="visited.has('general')" v-show="activeSection === 'general'" />
    <DependenciesPane v-if="visited.has('dependencies')" v-show="activeSection === 'dependencies'" />
  </template>
</TSettingsDialog>
```

两种不能混用：给了默认插槽，按分区命名的插槽就不渲染了。

**`flush`：面板自己管滚动和留白**。默认情况下右栏整体滚动，分区内容有 4px 24px 24px 的内边距，适合一列设置行。
面板自带固定在对话框底边的操作栏（「保存」「取消」，上分隔线通栏）时，操作栏不能跟着正文滚走，分隔线也不能缩进：加上 `flush`，
分区内容从分区名下面一直占到对话框底边，没有内边距，面板自己让正文滚动、自己留边距：

```vue
<TSettingsDialog v-model="open" v-model:active-section="section" :sections="sections" flush>
  <template #general>
    <div class="pane">                      <!-- display:flex; flex-direction:column; flex:1; min-height:0 -->
      <div class="pane__body t-ai-scroll">…</div>   <!-- flex:1; min-height:0; overflow-y:auto; padding:8px 24px -->
      <footer class="pane__actions">…</footer>       <!-- border-top 通栏; padding:12px 24px -->
    </div>
  </template>
</TSettingsDialog>
```

**手机上**：全屏，标题行（标题 + 关闭按钮）下面是一行横排的分区（可以横向滑动），再下面是打开的分区；不再是「分区列表 / 分区」两屏，
返回键或返回手势关掉整个弹窗（`TDialog` 的一层）。`TChatApp` 交给它的是侧栏抽屉用的同一个信号（`sidebarMobileBreakpoint` 或 `isMobile`）。
**焦点**：打开时落在面板上（不落在筛选框或分区里的输入框上，手机不会一打开就弹键盘），Tab 进入分区列表；关上后回到打开它的元素。
分区内容撑不宽右栏：放不下的行在栏内换行或横向滚动；`TSettingRow` 放不下时把控件换到标签下面。

`TChatApp` 的对应项：`settingsSections`（同样可以带 `group`、`badge`、`badgeLabel`）、`settingsTitle`、`settingsSearchable`、`settingsShowAccount`，
新增 `settingsBeforeClose`（即 `beforeClose`）与插槽 `#settings-lead`（即 `#lead`，插槽参数 `{ section }`）。

**`TSettingRow` 的标签**是一行会换行的 flex（行距 6px、元素间 8px，`min-width: 0`），`#label` 插槽可以在名字旁放图标或 `TTag`，
放不下时换到下一行；控件区同样是 flex，多个控件相隔 8px：

```vue
<TSettingRow description="完成任务时通知我。">
  <template #label><Icon icon="lucide:bell" />桌面通知<TTag tone="accent">新</TTag></template>
  <TSwitch v-model="notify" label="桌面通知" />
</TSettingRow>
```

**层栈：Escape 和 Tab 只给最上面那一层**。本包里接管 Escape 或困住焦点的浮层（设置弹窗、命令面板）与 `@tnzi/ui` 的 `TConfirm`、
`TAppShell` 抽屉共用 `@tnzi/ui` 的同一个层栈：在设置分区里打开的确认框按一次 Escape 只关它自己，Tab 留在它里面。消费方的对话框这样加入：

```ts
import { useFocusTrap, useOverlayLayer } from '@tnzi/ui'

// 困住焦点的对话框：一行就够，Escape 与 Tab 都经层栈
useFocusTrap(panelRef, () => open.value, { onEscape: close })

// 只接管按键、不困焦点的浮层（菜单、预览）：isTop 是「是不是最上面一层」
const { isTop } = useOverlayLayer(() => open.value, { onEscape: close, onKeydown: (e) => { /* 方向键… */ } })
```

按键请走这两个的回调，不要再自己往 `document` 上挂监听：自己的监听不在「一次按键只给一层」的保证里（最上面一层在它的回调里关掉，
浏览器会在两个监听之间刷新 Vue，下面那层就以为自己成了最上面一层）。没有加入层栈的模态（别的组件库的对话框）里发生的按键，
层栈不去接；控件已经 `preventDefault` 的 Escape（比如输入框里按 Escape 清空），层栈也不接。

**顶部留白**：页面顶部有一条浮层**内容**要让开的区域时（无边框窗口的标题栏，窗口按钮画在上面），在根元素上声明它的高度：

```css
:root { --tnzi-overlay-inset-top: 48px; }
```

设置弹窗与 `TDialog`（宽屏在这条下面的空间里居中，窄屏面板连同关闭按钮从这条下面开始）、命令面板、窄屏侧栏抽屉、嵌入的侧栏聊天
都让开这条，所以没有可点的东西落在窗口按钮底下；浮动聊天窗在矮视口下缩小，而不是顶进去。**遮罩则盖满整个视口，这条也一起变暗**：
只盖到这条下面时，标题栏下的会话标题亮着而其余全暗，读起来像出了错。没有 `app-region` 的遮罩不会切掉拖动区，所以标题栏仍能拖动窗口
（在这条上按下是拖窗口，而不是关掉弹窗）。要声明在 `:root`（或 `body`）上：这些浮层传送到 `body` 下，只继承那里的值。
窄屏面板另外按设备安全区留白（刘海只在这条留白没盖住时才补）。

**窗口边角**：窗口自己的按钮（无边框桌面窗口的最小化、最大化、关闭）画在页面右上角时，再声明那块的宽和高：

```css
:root {
  --tnzi-ai-window-inset-right: 138px; /* 右上角按钮占的宽度 */
  --tnzi-ai-window-inset-top: 48px;    /* 那一条的高度 */
}
```

- `TChatApp` 的顶栏与 `TGuestWelcome` 的顶栏右侧让开 `--tnzi-ai-window-inset-right`，所以顶栏右端的操作不会落在窗口按钮底下。
- `asideFullHeight` 的面板打开时右上角归面板：顶栏恢复普通的右边距，面板内容从 `--tnzi-ai-aside-inset-top` /
  `--tnzi-ai-aside-inset-right` 读到这块边角（等于上面两个值；面板不是全高时都是 `0px`），自己的头部按它们让位；
  `aside` 插槽的 `fullHeight` 同时说明面板此刻是不是全高。

```css
.my-aside__head {
  padding-top: var(--tnzi-ai-aside-inset-top, 0px);
  padding-right: var(--tnzi-ai-aside-inset-right, 0px);
}
```

不设这两个变量时什么都不变。它们与 `--tnzi-overlay-inset-top` 各管一件事：那一个让浮层内容让开整条标题栏，这两个让外壳本身让开右上角的按钮。

## 账号设置：头像与注销账号

`TAccountSettings`（`TChatApp` 内置设置页里的「账户」，或单独挂）的资料组顶部是**头像**（一个圆形的 `TImagePicker`）：点头像（或聚焦后回车 / 空格）选图，也可以把图片拖到头像上；
有头像时角上有一个移除按钮（悬停或聚焦时出现，触屏上一直在），上传期间头像上转圈、不接受点击；
PNG / JPEG / WebP、默认不超过 10 MB（`useAccountSettings({ avatarMaxBytes })`，与后端 `Identity:Avatar:MaxBytes` 一致）；
选好立刻居中裁成正方形、缩到 512 像素（`prepareAvatar`，传 `null` 原样上传）、经 `files/upload` 上传并写进资料，不等「保存」。
头像经 `controller.avatarUrl` 渲染：上传的文件优先，其次是第三方导入的链接，都没有显示首字母。需要 `Tnzi.Storage`；
没有它的部署传 `:allow-avatar="false"`。

页尾是**删除账号**：说明后果，点按钮展开确认行，必须输入账号邮箱（没有邮箱时是用户名，不区分大小写）才能点「删除我的账号」；
后端要求二次确认时，确认面板出现在这组里而不是页顶。不能删时用 `:deletion="{ blockedReason }"` 让按钮变灰并说明原因
（最后一个管理员、有效订阅）；`:deletion="false"` 整组不显示。后端拒绝的原因按错误码给出：`IDENTITY_USER_IN_USE`
（仍有记录以这个账号为主体）会连同服务端那句「是什么在用它」一起显示。

外壳里的头像与登录态不在这个控制器里，所以两件事要消费方接：

```ts
const controller = useAccountSettings({
  client,
  onProfileChange: () => auth.fetchUserProfile(),  // auth.avatar 随之更新，传给 TChatApp 的 accountAvatar
  onAccountDeleted: async () => { auth.clearAuth(); await router.replace('/login') },
})
```

用 `TChatApp` 内置设置页时，同样的两件事是事件：`@profile-change` 与 `@account-deleted`；`account-deletion` prop 对应上面的 `deletion`。

**部署不开放通用上传时**（`files/upload` 被关掉或换成了自己的上传端点），传 `uploadAvatar`。拿到的是裁剪后的文件，
返回存好的文件 id，框架照常把它写进 `avatarId`；抛错则把错误的 `message` 显示在头像那一行，资料不动：

```ts
const controller = useAccountSettings({
  client,
  uploadAvatar: async (file) => {
    const result = await myApi.uploadAvatar(file)  // 部署自己的端点
    if (!result.succeeded) throw new Error(result.message)
    return result.data.id
  },
})
```

这个 id 仍须是存储模块里本人读得到的文件：后端写入 `avatarId` 前照样校验归属、格式与大小。

## 导出子路径

| 子路径 | 内容 |
| --- | --- |
| `@tnzi/ui-ai` | 合集入口 |
| `@tnzi/ui-ai/plugin` | `defineChatApp`：登录路由 + 认证守卫 + 会话恢复（需 `vue-router`，可选 peer） |
| `@tnzi/ui-ai/auth` | 登录页 `TAuthPage` / 登录路由 `TAuthRoute`（登录**逻辑**在 `@tnzi/ui`） |
| `@tnzi/ui-ai/chat` | `TChatApp` 及会话流原语 |
| `@tnzi/ui-ai/components` | 消息 / 工具调用 / 推理 / 附件 / 浮层 / 控件等组件 |
| `@tnzi/ui-ai/controls` | 按钮、字段、下拉、开关、标签、提示条、菜单、分段控件、设置组与设置行、`vTooltip`、列表三态 `TStateView`、空页 `TEmptyState` / `TTemplateRow`、`useToast`、`applyControlsTheme`、`CONTROLS_ICON_NAMES`、`disableRemoteIcons`：背后只有 `vue` 与 `@iconify/vue`，不需要 `@tnzi/ui`、`@tnzi/core`（见「控件」与「只用控件」） |
| `@tnzi/ui-ai/dialogs` | `TDialog`、`TConfirmHost`、`useConfirm` / `createConfirmer`、`TImageViewer`：给只用控件的页面，样式在 `controls.css` 里；背后多了 `@tnzi/ui/headless` 与 `@tnzi/core` 的页面浮层栈（见「只用控件」） |
| `@tnzi/ui-ai/headless` | `useChatThreads`（整套对话循环）/ `useGlobalAiTheme` / `useChat` / `useStreamMarkdown` … |
| `@tnzi/ui-ai/adapters` | 后端 DTO → 视图模型（`toChatMessage` / `toThreadItem` / `toMessageRole`） |
| `@tnzi/ui-ai/workflow` | 工作流 DAG 可视化（`@vue-flow/core`，懒加载） |
| `@tnzi/ui-ai/widget` | 内联小部件：`createWidgetToolRenderers({ runnerUrl })` 把 agent 的 `show_widget` 调用画成沙箱里的可交互 HTML；`TWidgetFrame` 可单独用来显示任意 HTML 页面。小部件的表单照常触发 `submit`（沙箱有 `allow-forms`，运行页的 `form-action 'none'` 与桥接让表单哪儿也去不了），`hostContext.locale` 取 `locale` prop、应用随词典提供的语言或 `<html lang>`，变了就发 `host-context-changed`（运行页、安全模型与服务端配置见框架文档「AI 工具 · 内联小部件」） |
| `@tnzi/ui-ai/icons` | 本包（及其 @tnzi 依赖）会渲染的 Iconify 名字清单 + `bundleTnziIcons` / `selectTnziIcons` / `disableRemoteIcons`（生成；离线打包图标用，见下） |
| `@tnzi/ui-ai/embed` | 嵌入式小挂件模式 |
| `@tnzi/ui-ai/theme`、`/theme/*` | 运行时主题覆盖层 |
| `@tnzi/ui-ai/i18n` | 翻译引擎（`createAiI18n` / `createAiI18nPlugin` / `useAiI18n` / `useAiLocale` / `formatAiMessage` / `matchAiLocale`），与词典分开 |
| `@tnzi/ui-ai/locales`、`/locales/*` | 语言包（按需动态导入） |
| `@tnzi/ui-ai/utils` | 格式化与 markdown 归一化 |
| `@tnzi/ui-ai/vite` | `@tnzi/core/vite` 的转导出：`tnziSingletons()`，`link:` 安装下让 `vue`（及应用装了的 `vue-router`）只解析到应用自己的一份；返回类型不引用 vite 的类型，Vite 7 / 8 都能直接用 |
| `@tnzi/ui-ai/style.css` | 打包样式（必需引入） |
| `@tnzi/ui-ai/controls.css` | 只含控件的样式表，给只用 `@tnzi/ui-ai/controls` 与 `@tnzi/ui-ai/dialogs` 的页面，代替 `style.css`：控件、它们打开的菜单 / 弹出层 / 提示消息、对话框、确认与图片查看器（见「只用控件」） |

## 图标：不向第三方取

组件经 `@iconify/vue` 渲染图标，它遇到本地没有的名字会去 Iconify 公共 API 取：断网、内网或桌面 webview 里图标成了空白方块，
联网时每次加载都告诉第三方用户在看什么。把本包会渲染的图标打进包里，再关掉远程取图：

```ts
import { bundleTnziIcons, disableRemoteIcons } from '@tnzi/ui-ai/icons'
import { icons as lineMd } from '@iconify-json/line-md'
import { icons as lucide } from '@iconify-json/lucide'
import { icons as mdi } from '@iconify-json/mdi'
import { icons as simpleIcons } from '@iconify-json/simple-icons'

bundleTnziIcons([lineMd, lucide, mdi, simpleIcons])
disableRemoteIcons() // 之后没注册过的图标渲染为空白，不再发请求
```

应用自己的图标（工具渲染器、导航项、数据里存的名字）同样 `addCollection` 进来；漏掉的那个在开发时就是一个空白方块，而不是生产里的一次外发请求。
整份集合会进产物（这四个未压缩合计约 10 MB），在乎体积就在构建时用 `selectTnziIcons` 只挑出用到的那些，做法见框架文档「离线运行：把框架用到的图标打进包里」。
图标数据由应用安装（`@iconify-json/*`），框架只发布名字，版本与各集合的许可证义务因此留在应用侧。
只用 `@tnzi/ui-ai/controls` 的页面只需控件自己的 `CONTROLS_ICON_NAMES`，做法见「只用控件」。

## 发送时的滚动

`TChatApp` 的 `scrollMode` 决定发出一条消息、回答流式输出时对话怎么滚。两种模式共用同一套规则：

- 打开一个会话（或切回来）时停在最新一轮，切回一个仍在运行的会话时同样停在底部并接着跟随。
- 视图在底部时**跟随**：回答变长、工具步骤到达、输入框变高、手机键盘弹出让视图变矮，最新的内容都留在输入框上方。
  滚动在内容变化的同一帧写入，不做动画，所以不会抖。
- 读者表现出要看别处时**立即停止跟随**，按动作判断而不是按位置：滚轮或手指往上、向上的按键（PageUp、↑、Home、Shift+空格）、
  拖滚动条往上、在对话里展开一个折叠块（工具步骤、`<summary>`，展开的东西留在点它的地方）。
  里层还能往上滚的区域（长代码块）吃掉的手势不算；上方内容收起导致的位移也不算。输入框里的按键与菜单不算。
- 读者自己回到底部附近（`threshold`，默认 50px）时恢复跟随。
- 最后一条消息的末尾落在输入框下方时，输入框上方出现「滚动到最新消息」按钮；点它平滑滚到底部，并从那里开始跟随。
  末尾回到视野里它就消失，不管是滚动、展开或收起工具步骤、图片加载还是窗口大小变化造成的（`useLatestInView`）。
- 自动滚动只会往下，从不把视图往上拉。

两种模式的区别只在发送那一刻：

| `scrollMode` | 发送之后 | 适合 |
|---|---|---|
| `'chat'`（默认） | 发出的消息出现在输入框上方，**上一条回答的结尾仍在它上面**；回答往下长时视图跟着走，直到这条消息到达视图顶部（桌面 24px、手机 12px）就停下，长回答从开头读。读者自己滚过这一点到底部，就改为跟随底部 | 对话式聊天 |
| `'follow'` | 滚到底部并一直跟随最新输出，像终端 | 一轮是一长串工具调用的编码 agent，要看的总是最新输出 |

```vue
<!-- 编码 agent：跟随最新输出 -->
<TChatApp scroll-mode="follow" ... />
```

判定「发出了一条」看的是对话末尾出现一条新的用户消息（来自输入框、编辑，或产品自己发的）；切换会话、打开会话、
一次到达多轮的转录都不算。重新生成沿用这一轮的上限。自己用 `#message` 插槽画消息不影响这一点：每条消息外面都包着一个带 `data-message-id` 的块。

对话的上下两端都渐隐：底部消息淡入输入框背后，顶部在滚动过之后淡入顶栏下方（停在最顶端时不加，免得压暗第一条消息）；渐隐层不拦点击。

`TChatBox` 有同一个 `scrollMode` 属性（默认 `'chat'`）。

底层是可单独使用的 headless 组合函数（`@tnzi/ui-ai/headless`）：

- `useAutoScroll(options?)`：`containerRef`、`isAtBottom`、`following`、`scrollToBottom(behavior?)`（到底部并跟随）、
  `followTurn(find, { topOffset })`（跟随，但不超过 `find()` 的元素到达顶部的位置）、`stopFollowing()`。
  选项 `threshold`、`follow`（`false` = 从不跟随，只量 `isAtBottom`）、`notContent`（滚动区里不是对话的部分的选择器，例如放在里面的输入框）。
  纯函数 `followTop(metrics)` 给出跟随时该滚到哪里。类型 `ChatScrollMode`。
- `useLatestInView`：`hiddenBelow`、`scrollToLatest()`，量的是最后一条消息的末尾在不在可见区域内，而不是滚动条到没到底。
- `useTurnAnchor`：把一条消息滚到视图顶部、在内容后留白让它停得住的旧阅读模型。`TChatApp` 已不再使用它，自己组装外壳、确实想要这种行为的消费方仍可直接用。

## 窗口不够宽：侧栏自动收成图标栏

宽屏上，对话一栏放不下时侧栏自己收成图标栏，地方够了再自己展开（`sidebarAutoCollapse`，默认开）。
判断用的是「侧栏展开时对话还剩多宽」：外壳的宽度减去展开的侧栏（`sidebarWidth`）、打开着的右侧面板（按它 70% 的上限算）
和产物面板。低于 `conversationMinWidth`（默认 560px）就收起，回到 `conversationMinWidth` + 80px 以上才展开。
这个量与侧栏现在是展开还是收起无关，收起本身不会让它重新变够，所以不会在边界上来回跳；那 80px 只吸收取整和来回拖动面板宽度的抖动。
窗口缩放、外壳自身尺寸变化（嵌在更窄的容器里也算）、右侧面板打开 / 关闭 / 拖动都会重新判断。

用户的选择优先：

- 只有自动收起的侧栏才会自动展开。用户自己收起的图标栏、自己隐藏的侧栏，多宽都不动，刷新后照旧（与此前一样记在 `sidebarStorageKey` 下）。
- 自动收起和展开**不写入** `sidebarStorageKey`：窗口碰巧窄的时候，不会被记成「收起」，下次在宽窗口打开仍是展开。
- 地方不够时用户手动展开，不会马上又被收起；直到地方够过一次之后，再变窄才照常收起。
- 窄屏（`sidebarMobileBreakpoint` 以下）的抽屉与此前相同，不受影响。

```vue
<!-- 对话列要宽一些的产品 -->
<TChatApp :conversation-min-width="680" />
<!-- 不要自动收起 -->
<TChatApp :sidebar-auto-collapse="false" />
```

自己用 `useSidebarState` 组外壳时，`@tnzi/ui-ai/headless` 的 `useSidebarAutoFit(sidebar, { room, enabled, minWidth, margin })` 是同一套规则，
`conversationRoom(...)` 算宽度，`decideSidebarFit(...)` 是纯判断。`useSidebarState` 多了 `autoCollapsed`（当前的图标栏是不是自动收起的）、
`choices`（用户经 `setMode` / `cycle` 选过几次，用来区分用户的选择与状态自己做的改变）和 `applyAutoMode('icon' | 'expanded')`（不记忆、不持久化的改变）。

## 落地页的布局：输入框不随建议移动

落地页（`TLandingPage`，`TChatApp` 的空对话）分三段：上方一段空白、中间的标题区（`landing-plan`、标题、`landing-subline`、输入框）、
下方的建议与页脚（`landingChips` / `#landing-chips`，然后 `#landing-footer`）。**只有标题区居中**：上方空白与下方区域平分剩下的高度，所以

- 没有建议、一行、几行，建议晚到或换了一批，**标题和输入框都停在同一个位置**；建议始终紧贴在输入框下面。
  在两种模式之间切换（一种带两行建议、一种没有）时输入框不跳。
- 建议和页脚放不进下半部分时继续往下排，页面可以滚动到它们，标题区不因此上移；最后一项后面留出与页面底边距相同的空白。
- 窗口矮到连标题区都放不下时，页面从顶端开始滚动，没有内容被裁掉或叠在一起。
- 手机（`compact`）同样如此：同一视口里输入框的位置与建议无关。
- 会移动标题区的只有它自己的内容：`landing-plan`、标题、`landing-subline` 变高时，标题区上移变化量的一半。
  需要两种模式下输入框也对齐时，让这些内容在两种模式里一样高。
- 标题与输入框一样宽（`maxWidth`，默认 768px），并且是尺寸容器（`container-type: inline-size`）：`landing-headline` 插槽里的问候语
  可以用 `cqi` 按这块宽度定字号，例如 `font-size: min(36px, 7cqi)`，窗口窄了自己变小而不换行。

DOM：`.t-landing` 下依次是 `.t-landing__above`、`.t-landing__hero`、`.t-landing__below`（`.t-landing__chips`、`.t-landing__footer` 在最后这一段里）。

## 手机（窄屏）

视口窄于 `sidebarMobileBreakpoint`（默认 768px）时，`TChatApp` 按手机排版，宽屏不受影响：

- **侧栏是抽屉，打开就是完整侧栏**（品牌、导航、最近对话、账号），不会是图标栏：在宽屏上选的「收起成图标栏」不影响手机，
  抽屉的开关也不写回宽屏记住的那个模式。抽屉标题行的按钮在窄屏上是「关闭侧栏」。
  自己控制 `TCollapsibleSidebar` 的消费方同样如此：`isMobile` 时传进来的 `icon` 按展开绘制。
- **落地页标题变小**（26px；放在 `landing-headline` 插槽里、以 em 为单位的内容随之缩小）。**落地页的输入框仍是两行**，
  对话里的输入框从一行开始，两者都随文字增高到上限，输入文字 16px（iOS 会把页面放大到更小字号的输入框上）；
  页边距、顶栏、对话列的留白收紧，吸底输入框下方留出 Home 指示条的安全区。
  `TLandingPage` / `TThreadComposer` 单独使用时传 `compact` 得到同样的排版。
- **变的是布局，不是大小**：侧栏的行（导航、最近对话、账号）、顶栏与侧栏标题行的图标按钮是 44px 高的触摸目标，行文字 16px，
  不会是桌面那套 32px 行、13–14px 字缩在手机上。这些尺寸是 `.t-chat-app--narrow` 上的变量（`--tnzi-ai-row-height`、
  `--tnzi-ai-row-font-size`、`--tnzi-ai-icon-button-size`、`--tnzi-ai-row-action-size`、`--tnzi-ai-field-font-size`），
  组件以桌面尺寸为回退读取它们，要调整时在消费方覆盖。
- **外壳钉在可见区域上**（`pinNarrowViewport`，默认开）：外壳 `position: fixed` 盖住视觉视口（`usePinnedViewport` 跟着
  `visualViewport` 写入高度与顶部偏移，键盘弹出时随之缩短），页面关掉回弹（`overscroll-behavior`），只有对话列表滚动，
  输入框始终贴在屏幕底部、键盘打开时贴在键盘上方。外壳在手机上不是整页时（例如上面还有产品自己的顶栏）传 `false`。
- **`disclaimer` 那行不显示**；自己的 `#thread-footer` 插槽照常渲染（可能有功能），插槽属性 `narrow` 告诉它当前是不是窄屏。

- **抽屉滑入滑出，页面不动**：窄屏上的侧栏开着关着都是 `position: fixed`，不占页面的一列，只用 `transform` 平移（遮罩只变透明度，两者同一时长同一曲线），
  所以开关过程中主区每一帧的位置与宽度都不变、文字不重排。关着时它 `inert`、`visibility: hidden`，不可聚焦也不被读屏读到；
  系统开了「减少动态效果」时不做过渡。
- **返回键 / 返回手势先关最上面一层**（见下一节「返回键关浮层」）。
- **触屏手感**：触屏（`@media (hover: none)`）上点过的控件不留悬停样式，按下时立刻有一层按压反馈（iOS 也有），
  长按按钮、导航行、菜单项不选中文字、不弹系统菜单（消息正文、代码、输入框照常可选可编辑）；
  用手指打开的操作菜单（`TActionMenu`，包括会话行的「…」菜单）不把焦点框画在第一项上，用键盘打开时照旧。见下文「触屏」。

**关闭页面缩放**（可选）：`defineChatApp({ preventPageZoom: true })`，不用 `defineChatApp` 时调 `@tnzi/ui-ai/headless` 的
`preventPageZoom()`（返回撤销函数）。它改视口 meta（`maximum-scale=1, user-scalable=no`，也挡住 iOS 聚焦小字号输入框时的放大）、
根元素 `touch-action: pan-x pan-y` 与 `text-size-adjust: 100%`，并取消 Safari 的 `gesturestart` / `gesturechange` / `gestureend`
（iOS Safari 对双指缩放既不认 meta 也不认 `touch-action`）。默认关：页面缩放是一部分人读小字的方式，放弃它由产品决定。
自己处理双指缩放的组件（图片查看器、画布，`touch-action: none` 加 pointer / touch 或 Safari gesture 事件）不受影响，事件照常送达，
只是浏览器自己的页面缩放被拦下；依赖浏览器缩放整页的查看器在开启时无法工作。

**品牌方块里的图形大小**：`--tnzi-ai-brand-mark-glyph-size`（默认 18px，方块 32px），侧栏标题与图标栏两处都读它，
也可以经 `applyAiTheme({ brandMarkGlyphSize: '26px' })` 设置。要填满方块的标志（像应用图标那样）把它调大。

## 返回键关浮层

手机上（Android 的返回键、iOS 的边缘返回手势）按返回先关掉最上面的一层，而不是离开页面，一次一层：
`TChatApp` 的窄屏抽屉、`TDialog`（底部弹层与全屏，`TSettingsDialog` 的全屏弹层就是其中之一）、
`TAsidePanel` 的窄屏层、`TPopoverMenu` / `TActionMenu`（也就是账号菜单与会话行的「…」菜单）。宽屏上返回照旧是页面导航。

做法是每个打开的层在浏览器历史里占一条记录：地址不变，`history.state` 是当前那条的副本（路由器的字段原样保留）再加一个键
`__tnziBackLayer`。返回弹出这条记录，层走它自己原有的关闭路径：

- 用关闭按钮、遮罩、Esc 或选了菜单项关掉的层，会把自己那条记录退掉，之后按一次返回就回到上一页。
- 关层与导航同时发生（抽屉里点导航、菜单项打开新页面）时，退记录的那一步落地之前，`history.pushState` / `replaceState` / `go` /
  `back` / `forward` 先排队，落地后按原顺序执行，所以新页面压在原页面上，中间没有失效的记录：在新页面按返回回到原页面。
- 关不掉的层（`closable: false`、`busy`、`beforeClose` 返回 `false`、Esc 不关的对话框）保持打开，页面也不走。
- 层下面压着一个新页面时（层开着时发生了导航），返回先回到原页面，再按一次关这一层。

**窄屏上没有前进记录**。关层（任何方式）和页面之间的返回都会在当前位置前方留下记录，iOS 从右边缘向左滑就是前进，会滑到
失效的层记录或刚离开的页面。网页删不掉前进记录，只有 push 会截掉它们，所以窄屏上（宽度小于 768px，也就是层接管返回的时候）
模块退到当前条目的前一条，再把当前条目按原来的 `history.state` 和地址推回去：

- 这一步谁也听不到：它的 `popstate` 不传给后面任何监听（路由器不导航、不跑守卫，`createRouter` 之前注册的监听也收不到）。
  推回发生在这一步自己的 `popstate` 里、任何别的代码运行之前，所以没有代码会读到前一条的地址或 `history.state`（路由器
  守卫异步、push 前读 `history.state` 改写当前条目的，照样写在对的条目上）；这一步在路上时的 `pushState` / `replaceState` /
  `go` / `back` / `forward` 照上面的规则排队；document 的滚动位置原样保持
  （浏览器会在这一步恢复前一条的滚动，每一帧绘制前放回原处）。`KeepAlive` 里的页面不受影响。
- 只会退到本文档里、模块认识的条目，绝不会因此离开当前页面。
- 关层时立即做，和退掉层记录是同一步；返回之后等历史静下来一会儿（约 350ms 没有新的返回、没有手指按着）再做，不会撞上手势
  或连续的返回。
- **清不掉的情况**：当前条目就是模块认识的第一条（页面加载或刷新时所在的那条，或从本文档之外回来后的那条），前面没有可退的条目。
  有前进手势的平台（iOS、iPadOS）上，这时把当前条目在原地再推一次：前进记录没有了，代价是从这一页按返回离开应用时多一次返回
  （显示的还是这一页）；退到这条重复记录后不再重复推，前方会剩一条同一页的记录。其它平台（没有前进手势）前进记录留着；前进落到
  失效的层记录时就地留下，把它当作这一页的记录，不再弹回去。
- 宽屏不做这些，浏览器的前进照常。

**消费方要做的**：

1. **在创建路由器之前启动**。`defineChatApp()` 已经做了，照文档在 `createRouter()` 之前调用它即可。不用 `defineChatApp` 的，在
   `createRouter()` 之前调 `@tnzi/ui-ai/headless` 的 `installBackLayers()`。原因：同一地址之间的返回只有赶在路由器的 `popstate`
   监听之前处理才拦得住，而窗口上的监听按添加顺序执行（实测 Chromium 不分捕获与冒泡）。没有提前启动时层照样能关，但这一次返回也会
   传给路由器：路由器对同一地址再走一遍守卫；若此刻恰好有一次导航在排队（菜单项关菜单的同时打开新页面），路由器会被带回原地址。
2. **地址里的弹窗**（比如 `?settings=` 决定的设置弹窗）不用改：打开的那一刻地址刚变过（从上一次点按或按键到层打开之间地址变了），
   这个层就被认作随地址而来，不再自己占记录，返回仍然只按一次。要明确关掉时，`TDialog` / `TSettingsDialog` 传 `:close-on-back="false"`。
3. **自己的浮层接入**：组件里用 `useBackLayer(open, onBack, { enabled })`（`enabled` 通常是「窄屏」），组件外用
   `createBackLayer(onBack)`（`open()` / `close()` / `dispose()`）。`onBack` 走这个层原有的关闭路径；返回 `false`（或它的 Promise）
   表示不关，层保留并重新占一条记录。关层之后要立刻读 `history.state` 或 `location` 的，先 `await whenBackLayersSettled()`。
4. **不要用 `history.length` 判断能不能后退**：层开着时会多出记录，窄屏的第一页上也可能多一条重复记录。

## 页面与对话的滚动位置

`TChatApp` 记住每个页面自己的滚动位置：`#view` 插槽里的页面、内置的 Recents 页各按 `view` 记一份，回到这个页面时恢复，
没去过的页面从顶部开始（`#view` 的各页面共用一个滚动容器，原来换页后停在上一页的位置）。内容异步加载的页面，位置会一直保留到内容
够高为止；用户自己开始滚动（滚轮、触摸、按键、按下）就不再抢。从页面回到对话时，对话停在离开时的位置；离开时在底部的，回来照旧在底部
并跟随输出，发送与流式输出时的滚动规则不变。这一条在所有宽度上生效。

**`#view` 插槽里放 `<KeepAlive>`**：插槽所在的区块在第一次显示页面之后一直挂着，切到对话或 Recents 时只是隐藏，所以 KeepAlive
不会随区块一起被卸掉，页面回来时内容立刻就在。插槽属性 `{ view, active }`：`view` 是当前视图（对话时是 `'chat'`），`active`
表示区块正显示着。按 `view` 选页面，没有对应页面时什么都不渲染：

```vue
<TChatApp v-model:view="view">
  <template #view="{ view }">
    <KeepAlive>
      <component :is="pages[view]" v-if="pages[view]" :key="view" />
    </KeepAlive>
  </template>
</TChatApp>
```

插槽里不依 `view` 而定的内容，在对话打开时也保持挂载（隐藏着）。单独用 `useScrollMemory()`（`save` / `restore` / `cancel`）
可以给自己的容器加同样的记忆；`useAutoScroll` 的 `openAt` 让一个重新出现的对话从给定位置打开。

**自己铺满、自己滚动的页面**：页面区（`section.t-chat-app__view`）自己滚动，并且无论内容是否溢出都留着滚动条的位置
（`scrollbar-gutter: stable`），这样长页面与短页面之间切换时内容不会左右跳。铺满页面区、在里面自己滚动的页面（`height: 100%` 的
iframe 应用、自带分栏的邮件客户端）永远不让页面区滚动，留出的位置就成了右边一条空白（滚动条宽 8px 时，412px 宽的手机上页面只有 404px）。
这样的页面在 `setup` 里调 `useChatViewFill()`（`@tnzi/ui-ai/chat`，也在 `@tnzi/ui-ai/headless`）：它在屏幕上时页面区不滚动
（`overflow: hidden`）、不留滚动条位置，页面拿到整个宽度与高度；别的页面照旧。声明跟着组件走：卸载或被 `<KeepAlive>` 停用时撤回，
重新激活时再声明；传 ref 或 getter（`useChatViewFill(() => mode.value === 'app')`）可以只在某些状态下铺满。不在 `TChatApp` 里时它什么都不做。
不要再用 `:has()` 之类的规则从外面改 `.t-chat-app__view`。

```ts
// 一个铺满页面区的应用页
import { useChatViewFill } from '@tnzi/ui-ai/chat'

useChatViewFill()
// 模板：<iframe :src="src" style="display: block; width: 100%; height: 100%; border: 0" />
```

## 触屏

只在 `@media (hover: none)` 下生效，鼠标设备的外观与行为不变：

- **悬停样式只在能悬停的设备上**：构建时一个 PostCSS 插件（`tools/vite/hover-media.mjs`，在 `vite.config.ts` 的 `css.postcss` 里内联配置，
  不是 `postcss.config.js`）把每条 `:hover` 选择器包进 `@media (hover: hover)`；同一条规则里的 `:focus-visible`、`.is-menu-open`
  这些选择器留在原处。靠悬停才显示的操作，各自已有的 `@media (hover: none)` 规则让它们在触屏上一直可见。
  这只处理本包的样式，消费方自己的样式不受影响。
- **按下反馈**：外壳、侧栏行、顶栏按钮、会话列表与 Recents 的行、菜单项、消息操作按钮在手指按下时叠一层当前文字色 10% 的底色
  （`styles/touch.css`，用 `background-image` 叠加，不与组件自己的背景冲突）。iOS 要页面上有 `touchstart` 监听 `:active` 才生效，
  `TChatApp` 与两种菜单挂载时调 `enableTouchPress()` 装一个空的被动监听；单独使用别的组件时可自己调它。
- **长按**：按钮、导航行、菜单、侧栏与顶栏 `user-select: none` 且 `-webkit-touch-callout: none`；其中的输入框照常可选可编辑。
  浏览器自带的点按灰色闪烁（`-webkit-tap-highlight-color`）关掉了，由上面的按压反馈代替。

## 应用标记：整页规则

有几条规则属于整个页面，而不是某个组件：正文字体与画布、键盘焦点线、原生控件的明暗（`color-scheme`）、每个滚动区的细滚动条。
它们都写在 `<html>` 的标记 `data-tnzi-ai-app` 之下，所以**只引入 `@tnzi/ui-ai/style.css` 不会改动页面**：嵌入 ui-ai 挂件的宿主页、
懒加载某个 ui-ai 组件的后台应用保持自己的样子。

谁打标记：

- `defineChatApp()`：调用时打上，整个应用生命周期都在（登录页也算）；
- `TChatApp`：挂载期间；
- 自己用组件搭外壳的应用：启动时调一次 `markAiApp()`（`@tnzi/ui-ai/headless`，返回释放函数；计数，两个外壳先后挂载不会互相摘掉），
  或者直接在 `index.html` 的 `<html>` 上写 `data-tnzi-ai-app`（页面原有的标记不会被释放函数摘掉）；
- 嵌入式挂件（`createTnziChat`）**不**打标记，不改宿主页。

标记下的规则（`src/styles/page.css`，滚动条在 `index.css`）：

| 规则 | 内容 |
|---|---|
| `body` | `margin: 0`，背景 `--tnzi-ai-bg`，文字 `--tnzi-ai-text`，字体 `--tnzi-ai-font-body`，`-webkit-font-smoothing: antialiased`。`@tnzi/ui` 的 reset 把 body 画成后台的冷灰画布，ui-ai 应用是自己的暖色画布 |
| `:focus-visible` | `outline: var(--tnzi-ai-focus-outline)`，`outline-offset: 2px`。包在 `:where()` 里，特异度等于裸 `:focus-visible`，组件自己的焦点样式照常胜出 |
| `color-scheme` | 浅色 `light`，`.dark` / `[data-theme='dark']` 下 `dark` |
| 滚动条 | 页面里每个滚动区都是细滚动条，见下一节 |

门禁 `__tests__/conventions/page-wide-rules.test.ts`：本包样式表里凡是触及整页的选择器（`html`、`body`、`*`、裸 `:focus-visible`、`:root` 后代）都必须在这个标记之下。

**整页默认值**（`styles/index.css`，都可在 `:root` 上覆盖，或经 `applyAiTheme`）：

| 令牌 | 默认 | 说明 |
|---|---|---|
| `--tnzi-ai-font-body` | 系统 UI 字体，列出 `PingFang SC`、`Microsoft YaHei UI`、`Microsoft YaHei`、`Noto Sans SC` | 中文不再交给浏览器的回退字体 |
| `--tnzi-ai-font-display` | 衬线（Libre Baskerville 回退 Georgia）；`:root:lang(zh)` 下为中文无衬线 | 中文标题用衬线会回退成细宋体。`:lang(zh)` 覆盖 zh-CN、zh-TW、zh-Hans，看的是页面的 `lang`。在中文页面上换标题字体要写 `:root:lang(zh)` 或用 `applyAiTheme({ fontDisplay })` |
| `--tnzi-ai-backdrop` | `rgb(0 0 0 / 0.32)` | 模态遮罩，轻一些 |
| `--tnzi-ai-backdrop-blur` | `0px` | 遮罩不模糊，页面清楚地留在对话框后面 |
| `--tnzi-ai-focus-outline` | `2px solid var(--tnzi-ai-accent-text)` | 键盘焦点线 |
| `--tnzi-ai-tooltip-fg` | `var(--tnzi-ai-bg)` | 提示气泡的字色（原为 `--tnzi-ai-surface`） |
| `--tnzi-ai-switch-knob` | `#ffffff` | 开关关闭时的旋钮 |
| `--tnzi-ai-image-veil` | `rgb(0 0 0 / 0.45)` | `TImagePicker` 图上的遮罩（悬停、聚焦、拖入、忙碌时） |
| `--tnzi-ai-on-image` | `#ffffff` | 遮罩上的相机图标与转圈 |
| `--tnzi-ai-image-viewer-backdrop` | `rgb(0 0 0 / 0.9)` | `TImageViewer` 的深色底，也是它的按钮圆底与工具栏底的来源 |
| `--tnzi-ai-wave` | 12×6 的波浪 SVG | 波浪分隔线的遮罩（见下） |
| `--tnzi-ai-page-max-width` | `1040px`（未变） | 内容页的阅读宽度；对话列仍是更窄的 `--tnzi-ai-content-width`（820px） |

## 页面小件：流入式标签与波浪分隔线

**流入式标签**（`.t-flow-tabs` / `.t-flow-tab`）：放在一条灰色操作条上，选中的标签是下方内容的表面色，两者之间没有线，标签底部两侧圆弧过渡到内容里。
条的两端各留 8px 给圆弧。标签自己的尺寸、内边距、字由页面定，这两个类只画衔接：

```html
<div class="t-flow-tabs">
  <button class="t-flow-tab is-on">文件</button>
  <button class="t-flow-tab">改动</button>
</div>
```

**标签在下**（`.t-flow-tabs--bottom`）：终端这类面板，标签条在内容下方，选中的标签从上方内容垂下来，底部是圆角，两侧圆弧朝上过渡到内容里：

```html
<div class="terminal-screen">…</div>
<div class="t-flow-tabs t-flow-tabs--bottom">
  <button class="t-flow-tab is-on">bash</button>
  <button class="t-flow-tab">node</button>
</div>
```

内容区自己的下边不要有圆角，否则圆弧接不上。

**波浪分隔线**（`.t-wave-divider`）：页面或对话里两部分之间的一条波浪细线。线是 `--tnzi-ai-wave` 做遮罩、底色 `currentColor`，
所以颜色由元素的 `color` 决定（默认 `--tnzi-ai-border-strong`），明暗都对。里面放文字时线分在文字两侧，文字用三级文字色：

```html
<div class="t-wave-divider" role="separator"></div>
<div class="t-wave-divider"><span>后台任务完成后接着说</span></div>
```

## 滚动条

ui-ai 画出来的每个滚动区都是同一种细滚动条（`--tnzi-ai-scrollbar-size` / `-thumb` / `-thumb-hover`，亮暗两套），
包括被 teleport 到 `<body>` 的设置弹窗、命令面板和操作菜单，以及脱离 `TChatApp` 单独使用的组件。
规则只挂在四个钩子上，不碰没有打标记的页面的 `*`：

- `:root[data-tnzi-ai-app]`：用 ui-ai 搭的应用的整个页面（见「应用标记：整页规则」），应用自己的页面与对话框也是同一种滚动条；
- `.t-chat-app`：外壳与其中的一切；
- `.t-ai-scroll-scope`：这个元素与其中的一切。浮层的根上都有它，所以消费方放进设置分区插槽里的内容也一致；
- `.t-ai-scroll`：这个元素本身。ui-ai 的每个滚动容器都带着它。

消费方自己的页面要一样的滚动条时，可以把自己的容器加上 `t-ai-scroll`（或在根上加 `t-ai-scroll-scope`），
或者用同样的变量写一条自己的规则。

## 主题

`src/styles/index.css` 是调色板的唯一真值源：`:root` 声明浅色 `--tnzi-ai-*`，
`.dark` / `[data-theme="dark"]` 声明深色。明暗切换就是根元素上的 class 切换
（`TChatApp` 的 `autoApplyTheme` 默认开启会自动做）。

有宿主 `@tnzi/ui` 主题上下文时（`defineChatApp()` 默认提供，`createTnziUi()` 也提供），
`TChatApp` 改为设置该上下文的模式，而不是只切 class：上下文把 `--tnzi-base-text` / 容器 / 边框等
随明暗变化的 `--tnzi-*` 变量以内联样式写在 `<html>` 上，优先级高于样式表的 `.dark` 块，
只切 class 会让这些变量停在上下文原来的模式。全局外观快照的品牌主色（`ui.primary`）同样经该上下文的
`setColor` 生效，色阶与 `-rgb` 随之变化，明暗切换也不会把它冲掉。

**强调色的两种用法各有一个变量**，因为强调色就是品牌主色，可能是任何颜色（浅黄放在浅色画布上当文字约 1.3:1，上面压白字约 1.4:1）：

| 用法 | 变量 |
| --- | --- |
| 强调色填充（发送按钮、主按钮、头像）**上面**的文字和图标 | `--tnzi-ai-on-accent`：白色，主色浅到白字低于 3:1 时换成近黑墨色 |
| 强调色**当作**文字或图标的颜色（链接、侧栏当前项、勾选标记） | `--tnzi-ai-accent-text`：同一色相，在当前明暗下对画布、侧栏都读得到 4.5:1；本来就读得到的颜色原样不动 |

两者都跟着 `@tnzi/ui` 的主色重算（`--tnzi-on-primary` 与 `--tnzi-primary-text`），运行时换主色不用刷新。
产品自己的强调色文字也用 `var(--tnzi-ai-accent-text)`，不要写 `var(--tnzi-ai-accent)`（本包自己的源码由一道门禁守着这条）。
状态色填充（失败的阶段、子任务徽标）上的文字是单独的 `--tnzi-ai-on-status`（白色），不随品牌色变。
`applyAiTheme({ accent })` 只换填充色，`onAccent` / `accentText` 仍跟着主色；要整套跟着变，用主题上下文换主色。
没有主题上下文、只用控件的页面（桌面窗口）用 `@tnzi/ui-ai/controls` 的 `applyControlsTheme({ mode, accent })`，见「只用控件」。

**圆角档位**：控件按一套圆角画，页面自己的卡片、面板、窗口框架用同一套令牌就与控件对齐（`controls.css` 与 `style.css` 都带）：

| 令牌 | 默认 | 控件里用在 |
| --- | --- | --- |
| `--tnzi-ai-radius-xs` | 4px | 勾选框、字段里的小标签 |
| `--tnzi-ai-radius-sm` | 6px | 提示气泡、提示消息的按钮、多选里的标签 |
| `--tnzi-ai-radius-md` | 8px | 按钮、输入框、下拉、菜单项、分段控件：控件本身的圆角 |
| `--tnzi-ai-radius-lg` | 12px | 菜单、弹出层、提示条：面板与卡片 |
| `--tnzi-ai-radius-pill` | 999px | 开关、标签、进度条 |
| `--tnzi-ai-modal-radius` | 20px | 对话框（跟随 `@tnzi/ui` 的 `--tnzi-radius-xl`） |

控件读这些令牌，改一档（`applyAiTheme({ radiusMd: '6px' })` 或直接覆盖变量）控件跟着变。少数中间值（大号按钮的 10px、停靠小窗的 14px）仍是字面量。

嵌入式挂件（`createTnziChat`）挂在自己的应用里，上面不需要任何 provider：它画的一切都取 `--tnzi-ai-*`，
明暗跟随 `<html>` 的 `.dark` / `[data-theme="dark"]`。

## ⚠️ 组件改动没有自动化浏览器覆盖

本包的 SFC 不参与单测覆盖率（需要真实 DOM + 用户交互）。原先承担这部分的 playground 与
Playwright 规格**已于 2026-08-01 删除**，因此**单测全绿不代表 SFC 改动是对的**。
唯一的可视验证入口是消费应用的 chat 界面（dev 端口 6174）。

## 文档

- [@tnzi/ui-ai 包文档](https://tnzi.cc/docs/modules/ui-ai) — 入口、主题令牌、扩展点
- [架构](https://tnzi.cc/docs/architecture) — 五包分层与依赖方向

## License

MIT
