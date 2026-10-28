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
`TChatApp` 的 `enableAttachments` 关掉时，回形针、粘贴、拖放**三条入口都失效**，不会出现发不出去的芯片。
绑定了外部 CLI 运行时的 agent 的对话里，附件随用户消息存档，重开会话时照样显示（内容经 `resolveAttachmentUrl` 读，见下文）；
内建引擎的对话只按 `message` 文本存用户消息，重开会话时看不到当初的附件。

**打开会话**：`selectThread(id)` 取回会话内容之后，才把 `activeThreadId` 与 `messages` 一起切过去，所以屏幕上的会话始终就是
发送的去处。加载失败时停在原会话（经 `onError` 报告，那一行可以再点）；加载期间发出的消息留在原会话，并取消这次切换。
加载期间 `openingThreadId` 指着正在打开的那个会话：交给 `TChatApp` 的 `openingThreadId`，点下去那一行当场高亮（图标换成转圈），
主区换成这个会话的加载态（`TThreadLoading`，顶栏写它的标题；会话不在 `threads` 里、而在产品自己的 `navGroups` 里时，
由 `threadTitleOf(threadId)` 告诉外壳它叫什么），发送等内容到了再放开；不接的话，内容到达之前屏幕上什么都不变，
慢网络下读起来像点了没反应。加载期间再点屏幕上的那个会话（当前会话，或新会话第一轮的乐观行）表示「留在这里」，取消那次打开；
再点正在打开的那个不会再取一次，返回同一个 promise。
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
高度是品牌名字号的 1em，宽度按图标自己的宽高比，在文字原来占的那一行里垂直居中，行高保持不变，所以旁边的标记、收起按钮和下面的导航都不动；
图标里的 `currentColor` 跟着明暗主题走，其它固定颜色（比如 i 上那个黄点）保持原样。字母要看起来与正文一样大，就在 viewBox 里留出上下空白。
`brandName` 仍以视觉隐藏的文字留在原处，读屏软件读的是它，所以两个都要给；设置里的品牌行、顶栏的兜底标题照旧用 `brandName` 的文字。
访客欢迎页（`TGuestWelcome`）与登录页（`TAuthPage` / `TAuthRoute`）接受同名的 `brandWordmark`，效果相同。

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

**语音输入**：输入框的麦克风按钮用浏览器的 Web Speech 识别（`enableVoice`，默认开；`voiceLang` 是识别语言，改了下次开始就生效）。
浏览器没有识别器、页面不是安全上下文（https 或 localhost）、或者权限策略关掉了麦克风（`Permissions-Policy: microphone=()`、iframe 没给 `allow="microphone"`）时不显示这个按钮。
第一次点击时浏览器会问要不要给麦克风。识别停下的每一种原因都有一句话，说发生了什么、能怎么办（词典里的 `composer.voice*`）：
麦克风被这个网站禁止了（去地址栏左边的站点设置里允许）、没获得权限、浏览器不在这里提供识别服务、没有可用的麦克风、连不上识别服务、没听到声音、不支持这种语言。
有识别器却没有识别服务的宿主（嵌入式 webview）在第一次使用时报「连不上识别服务」或「没获得权限」；确定用不了的宿主里把 `enableVoice` 关掉。
自己做输入框时用 `useVoiceInput`，`error` 是 `VoiceInputErrorCode`，文案自己定。

**账号菜单的子菜单**：`userMenuExtraItems`（或 `TUserMenu` 的 `items` / `extraItems`）里带 `children` 的项点开是一层子菜单，
在原地展开、顶上一行返回，而不是飞出到侧边（侧栏只有 300px，手机上没有地方飞）。`checked` 标出当前选中的那一项，
所以语言、主题这类选择器可以直接放进账号菜单。选中子项时 `user-menu` 发的是子项的 `id`，整棵树里的 id 要唯一（`lang:en`、`theme:dark`）。

```ts
const extraItems = computed(() => [
  { id: 'language', label: t('language'), value: currentLanguageName.value, children: [
    { id: 'lang:en', label: 'English', checked: locale.value === 'en' },
    { id: 'lang:fr', label: 'Français', checked: locale.value === 'fr' },
  ] },
])
```

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
- 筛选与分组菜单：状态（活跃 / 已归档 / 全部）、最近活动（1 / 3 / 7 / 30 天 / 全部）、分组（日期 / 不分组）、排序（名称 / 创建时间 / 最近活动）。
  状态、时间窗与排序随每一页发给服务端，翻页始终正确；分组只排列已经加载的行。四项选择按浏览器记住
  （`useChatThreads({ recentsStorageKey })`，默认 `'tnzi-ui-ai-recents'`，`null` 不记）。
- 一个应用里有多个 Agent 且各管各的列表时，`useChatThreads({ listAgentIds: () => [agentId] })` 把侧栏列表、搜索和完整最近对话页都限定到这些 Agent（服务端先过滤再分页）；不传则不限。
- 每行的「…」菜单：重命名（就地编辑）、归档 / 取消归档、删除（删除仍是行内确认，仍走 `delete-thread`）。
- 「查看全部」把 `view` 设为 `'recents'`（导出常量 `RECENTS_VIEW_ID`，经 `update:view`）。有 `recents` 时外壳自己渲染完整的
  最近对话页（`TRecentsPage`：活跃 / 已归档、搜索、最近活动时间、翻页、同样的行操作），`#recents` 插槽可以换掉它；
  自己按 URL 路由视图的产品把这个 id 映射到自己的地址即可。
- 自己先过滤掉一部分会话再交 `threads` 的产品，用 `recents-include` 把同一个判断也交给完整页面。
- **顶栏标题**：打开一个已保存的会话时，顶栏的标题点一下就地变成输入框（有边框与聚焦光圈；宽度跟着文字走：打开时与原标题一样宽，输入时随之变宽变窄，
  最少约几个字的宽度，最多到顶栏剩余的空间为止，再长就在框内滚动，不会挤到顶栏右侧的按钮；
  整句选中；回车或离开保存，Esc 取消，空的不保存），
  标题后的小箭头打开菜单：重命名、归档 / 取消归档、删除（在顶栏里就地确认，删除仍走 `delete-thread`）。重命名走 `recents.rename`，
  侧栏、最近对话页与顶栏同步更新。新对话的落地页、还在创建或正在打开的会话上标题仍是普通文字。组件单独导出为 `TThreadTitle`。
  框架没有「置顶」与「分享会话」的数据，这两项不出现。
- 完整的最近对话页：搜索在「活跃 / 已归档」那一行里就地展开（最宽 384px，x 或 Esc 收起并清空），每个会话一行，标题在左、最近活动时间在右。
- 不传 `recents` 时列表与此前一样：没有筛选菜单、没有查看全部，行菜单里只有删除，顶栏标题不可编辑。

归档要服务端的 `AgentThread.ArchivedTime` 列，升级后消费方要生成一次迁移（见升级说明）。

- **停止**：`abort()` 停下本地的流；这一轮是外部 CLI 运行时（流的第一个事件交出了运行 id，`activeRunId`），同时请服务端取消它。只断开连接不会让服务端停下。
- **刷新之后接上**：打开一个还有运行在跑的会话时，`useChatThreads` 找到那次运行并从头接上它的流，回答接着长出来，停止按钮照样能用。
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

- **外框**：宽屏是右侧一栏，左缘有可拖动的分隔条（`role="separator"`，可聚焦；左右方向键调 16px，加 Shift 调 64px，Home / End 到最小 / 最大）。窄屏（`isMobile`）是盖住对话的整层，顶部有返回按钮，Esc 也能关。面板只在对话视图里出现，落地页和产品自己的 `view` 页里没有。
- **按对话记住**：开 / 关、宽度和当前页签按对话（`activeThreadId`）分别记在 `localStorage`（`asideStorageKey`，默认 `tnzi-ui-ai-aside`，`null` 则只在内存里；最多记 200 个对话）。没动过的对话用默认值：`asideDefaultOpen`（默认关）、`asideDefaultWidth`（420）、`asideMinWidth` / `asideMaxWidth`（280 / 720）、`asideDefaultTab`。还没有 id 的新对话共用一个草稿位，第一轮建出 id 后把状态带过去。
- **插槽参数**：`tab`、`setTab(id)`、`close()`、`narrow`、`threadId`。页签 id 由产品定义，框架只负责记住。
- **以代码打开**：`TChatApp` 的模板引用上有 `openAside(tab?)`、`closeAside()`、`toggleAside()`；对话里的渲染器（工具调用行、`message` 插槽里的组件）用 `useChatAside()`（`@tnzi/ui-ai/chat`）：`useChatAside()?.show('changes')`。没有面板时它返回 `null`。状态变化时发 `aside-change`（`{ open, tab }`）。
- **顶栏开关**：`showAsideToggle`（默认开）。想自己放按钮就关掉它，再用上面的方法。
- **配色**：只用 `--tnzi-ai-*` 令牌，换主题即跟着换。
- 底层积木：`TAsidePanel`（外框）和 `useAsidePanel`（无头状态），自己组装外壳时可单独用。

## 浮层：设置弹窗、层栈、顶部留白

**设置弹窗在窄屏上**：`TSettingsDialog`（也就是 `TChatApp` 的设置）在视口窄于 `narrowBreakpoint`（默认 768）时是一块全屏面板，
分区列表和选中的分区是两屏，分区那一屏顶上有返回，关闭按钮在面板自己的标题行里，不会压在列表上；`narrow` 可以强制两种布局之一。
`TChatApp` 交给它的是侧栏抽屉用的同一个信号（`sidebarMobileBreakpoint` 或 `isMobile`），两者不会一个窄一个宽。
打开时传了 `activeSection` 就直接进那一分区，不传则从列表开始：`TChatApp` 的「设置」从列表开始，账号菜单的「账户」直接进账户。
分区内容撑不宽内容栏：放不下的行在栏内换行或横向滚动，不再被弹窗边缘切掉；`TSettingRow` 放不下时把控件换到标签下面。

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
浏览器会在两个监听之间刷新 Vue，下面那层就以为自己成了最上面一层）。没有加入层栈的模态（例如 naive 的 `NModal`）里发生的按键，
层栈不去接；控件已经 `preventDefault` 的 Escape（比如输入框里按 Escape 清空），层栈也不接。

**顶部留白**：页面顶部有一条浮层**内容**要让开的区域时（无边框窗口的标题栏，窗口按钮画在上面），在根元素上声明它的高度：

```css
:root { --tnzi-overlay-inset-top: 48px; }
```

设置弹窗（宽屏在这条下面的空间里居中，窄屏面板连同返回 / 关闭按钮从这条下面开始）、命令面板、窄屏侧栏抽屉、嵌入的侧栏聊天
都让开这条，所以没有可点的东西落在窗口按钮底下；浮动聊天窗在矮视口下缩小，而不是顶进去。**遮罩则盖满整个视口，这条也一起变暗**：
只盖到这条下面时，标题栏下的会话标题亮着而其余全暗，读起来像出了错。没有 `app-region` 的遮罩不会切掉拖动区，所以标题栏仍能拖动窗口
（在这条上按下是拖窗口，而不是关掉弹窗）。要声明在 `:root`（或 `body`）上：这些浮层传送到 `body` 下，只继承那里的值。
窄屏面板另外按设备安全区留白（刘海只在这条留白没盖住时才补）。

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
| `@tnzi/ui-ai/widget` | 内联小部件：`createWidgetToolRenderers({ runnerUrl })` 把 agent 的 `show_widget` 调用画成沙箱里的可交互 HTML（运行页、安全模型与服务端配置见框架文档「AI 工具 · 内联小部件」） |
| `@tnzi/ui-ai/icons` | 本包（及其 @tnzi 依赖）会渲染的 Iconify 名字清单 + `bundleTnziIcons` / `selectTnziIcons` / `disableRemoteIcons`（生成；离线打包图标用，见下） |
| `@tnzi/ui-ai/embed` | 嵌入式小挂件模式 |
| `@tnzi/ui-ai/theme`、`/theme/*` | 运行时主题覆盖层 |
| `@tnzi/ui-ai/i18n` | 翻译引擎（`createAiI18n` / `useAiI18n` / `formatAiMessage`），与词典分开 |
| `@tnzi/ui-ai/locales`、`/locales/*` | 语言包（按需动态导入） |
| `@tnzi/ui-ai/utils` | 格式化与 markdown 归一化 |
| `@tnzi/ui-ai/style.css` | 打包样式（必需引入） |

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

## 发送时的滚动

`TChatApp` 打开一个会话时停在最新一轮。发出一条消息时，这条消息被滚到视图顶部附近，回答在它下面往下长；
视图**不追着回答的末尾走**，长回答从开头读，不用每次往回翻。回答还短的时候，消息下面留出一段空白，好让它能停在顶部；
回答长过一屏后这段空白归零。发出之后滚动位置归用户：回答流式输出时用户滚到哪就停在哪。
最后一条消息的末尾落在输入框下方时，输入框上方出现一个「滚动到最新消息」按钮，点一下滚到刚好露出那条消息的末尾；
末尾回到视野里它就消失，不管是滚动、展开或收起工具步骤、图片加载、窗口大小变化还是发送后留白的伸缩造成的（`useLatestInView`）。
发送后留出的空白不算「下面还有内容」。
对话的上下两端都渐隐：底部消息淡入输入框背后，顶部在滚动过之后淡入顶栏下方（停在最顶端时不加，免得压暗第一条消息）；
渐隐层不拦点击。

判定「发出了一条」看的是对话末尾出现一条新的用户消息（来自输入框、编辑，或产品自己发的）；切换会话、打开会话、
一次到达多轮的转录都不算。底层是两个可单独使用的 headless 组合函数：`useAutoScroll({ follow: false })`
（打开时在底部、`isAtBottom` 跟着内容更新、但不跟随）、`useTurnAnchor`（留白高度 `reserve`、`anchor(find)`、`release()`）
与 `useLatestInView`（`hiddenBelow`、`scrollToLatest()`：量的是最后一条消息的末尾在不在可见区域内，而不是滚动条到没到底）。
自己用 `#message` 插槽画消息不影响这一点：每条消息外面都包着一个带 `data-message-id` 的块。

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

**关闭页面缩放**（可选）：`defineChatApp({ preventPageZoom: true })`，不用 `defineChatApp` 时调 `@tnzi/ui-ai/headless` 的
`preventPageZoom()`（返回撤销函数）。它改视口 meta（`maximum-scale=1, user-scalable=no`，也挡住 iOS 聚焦小字号输入框时的放大）、
根元素 `touch-action: pan-x pan-y` 与 `text-size-adjust: 100%`，并取消 Safari 的 `gesturestart` / `gesturechange` / `gestureend`
（iOS Safari 对双指缩放既不认 meta 也不认 `touch-action`）。默认关：页面缩放是一部分人读小字的方式，放弃它由产品决定。
自己处理双指缩放的组件（图片查看器、画布，`touch-action: none` 加 pointer / touch 或 Safari gesture 事件）不受影响，事件照常送达，
只是浏览器自己的页面缩放被拦下；依赖浏览器缩放整页的查看器在开启时无法工作。

**品牌方块里的图形大小**：`--tnzi-ai-brand-mark-glyph-size`（默认 18px，方块 32px），侧栏标题与图标栏两处都读它，
也可以经 `applyAiTheme({ brandMarkGlyphSize: '26px' })` 设置。要填满方块的标志（像应用图标那样）把它调大。

## 滚动条

ui-ai 画出来的每个滚动区都是同一种细滚动条（`--tnzi-ai-scrollbar-size` / `-thumb` / `-thumb-hover`，亮暗两套），
包括被 teleport 到 `<body>` 的设置弹窗、命令面板和操作菜单，以及脱离 `TChatApp` 单独使用的组件。
规则只挂在三个钩子上，不碰消费方页面的 `*`：

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
`setColor` 生效，色阶、`-rgb` 与 naive 控件随之变化，明暗切换也不会把它冲掉。

**强调色的两种用法各有一个变量**，因为强调色就是品牌主色，可能是任何颜色（浅黄放在浅色画布上当文字约 1.3:1，上面压白字约 1.4:1）：

| 用法 | 变量 |
| --- | --- |
| 强调色填充（发送按钮、主按钮、头像）**上面**的文字和图标 | `--tnzi-ai-on-accent`：白色，主色浅到白字低于 3:1 时换成近黑墨色 |
| 强调色**当作**文字或图标的颜色（链接、侧栏当前项、勾选标记） | `--tnzi-ai-accent-text`：同一色相，在当前明暗下对画布、侧栏都读得到 4.5:1；本来就读得到的颜色原样不动 |

两者都跟着 `@tnzi/ui` 的主色重算（`--tnzi-on-primary` 与 `--tnzi-primary-text`），运行时换主色不用刷新。
产品自己的强调色文字也用 `var(--tnzi-ai-accent-text)`，不要写 `var(--tnzi-ai-accent)`（本包自己的源码由一道门禁守着这条）。
状态色填充（失败的阶段、子任务徽标）上的文字是单独的 `--tnzi-ai-on-status`（白色），不随品牌色变。
`applyAiTheme({ accent })` 只换填充色，`onAccent` / `accentText` 仍跟着主色；要整套跟着变，用主题上下文换主色。

嵌入式挂件（`createTnziChat`）挂在自己的应用里，上面没有任何 naive 主题，因此自带一份：
Tnzi 调色板（不是 naive 的默认绿），明暗跟随 `<html>` 的 `.dark` / `[data-theme="dark"]`。

## ⚠️ 组件改动没有自动化浏览器覆盖

本包的 SFC 不参与单测覆盖率（需要真实 DOM + 用户交互）。原先承担这部分的 playground 与
Playwright 规格**已于 2026-08-01 删除**，因此**单测全绿不代表 SFC 改动是对的**。
唯一的可视验证入口是消费应用的 chat 界面（dev 端口 6174）。

## 文档

- [@tnzi/ui-ai 包文档](https://tnzi.cc/docs/modules/ui-ai) — 入口、主题令牌、扩展点
- [架构](https://tnzi.cc/docs/architecture) — 五包分层与依赖方向

## License

MIT
