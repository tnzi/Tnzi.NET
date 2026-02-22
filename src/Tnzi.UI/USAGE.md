# Tnzi UI 使用指南

> 本文档面向终端用户（使用 `@tnzi/*` 包构建应用的开发者），详细说明各包的安装、配置和使用方法。

## 目录

1. [概述](#概述)
2. [安装与配置](#安装与配置)
3. [@tnzi/core — 核心包](#tnzicore--核心包)
4. [@tnzi/naive-ui — 管理后台](#tnzinaive-ui--管理后台)
5. [@tnzi/shadcn — Web 应用](#tnzishadcn--web-应用)
6. [@tnzi/vant — 移动端](#tnzivant--移动端)
7. [完整示例](#完整示例)

---

## 概述

Tnzi UI 采用 **核心 + 适配** 分层架构：

```
@tnzi/core（核心层，与 UI 无关）
├── HTTP 客户端 + 中间件
├── 响应式状态管理器（Auth / User / App）
├── 无头交互控制器（Pagination / Selection / Sort / Form / DataQuery）
├── 业务 API 契约（Identity / Storage / Chat / Notification / ...）
├── 适配器接口（message / dialog / theme / router / storage）
└── 类型 / 枚举 / 工具函数 / Zod Schema

@tnzi/{naive-ui | shadcn | vant}（适配层，实现视图）
├── Vue 插件（一键安装）
├── T* 业务组件（TLoginForm / TDataTable / TAdminLayout / ...）
├── 适配器实现（message → toast, dialog → modal, theme → CSS）
└── Pinia Store 封装（委托 core/state）
```

**核心原则**：业务逻辑写一次（在 core），换 UI 库只需换包。

---

## 安装与配置

### 场景 1：企业管理后台（推荐 naive-ui）

```json
{
  "dependencies": {
    "@tnzi/naive-ui": "^0.1.0",
    "vue": "^3.5.0",
    "vue-router": "^4.5.0"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^5.0.0",
    "typescript": "^5.9.0",
    "vite": "^5.4.0",
    "vue-tsc": "^2.0.0"
  }
}
```

### 场景 2：面向外部的 Web 站点（推荐 shadcn）

```json
{
  "dependencies": {
    "@tnzi/shadcn": "^0.1.0",
    "vue": "^3.5.0",
    "vue-router": "^4.5.0"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^5.0.0",
    "autoprefixer": "^10.4.0",
    "postcss": "^8.4.0",
    "tailwindcss": "^3.4.0",
    "typescript": "^5.9.0",
    "vite": "^5.4.0",
    "vue-tsc": "^2.0.0"
  }
}
```

> shadcn 基于 Tailwind CSS，需要额外安装 `tailwindcss` + `postcss` + `autoprefixer`。

### 场景 3：移动端 APP / PWA（推荐 vant）

```json
{
  "dependencies": {
    "@tnzi/vant": "^0.1.0",
    "vue": "^3.5.0",
    "vue-router": "^4.5.0"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^5.0.0",
    "typescript": "^5.9.0",
    "vite": "^5.4.0",
    "vue-tsc": "^2.0.0"
  }
}
```

### 场景 4：仅使用 core（自行选择 UI 库）

```json
{
  "dependencies": {
    "@tnzi/core": "^0.1.0",
    "vue": "^3.5.0",
    "vue-router": "^4.5.0",
    "pinia": "^2.2.0"
  }
}
```

> 仅用 core 时，需自行选择 UI 库并实现适配器接口。

### 内置依赖说明

UI 包已内置所有第三方依赖，用户**无需手动安装**：

| 安装 | 自动获得 |
|---|---|
| `@tnzi/naive-ui` | `@tnzi/core`, `naive-ui`, `pinia`, `@vue/reactivity`, `zod` |
| `@tnzi/shadcn` | `@tnzi/core`, `shadcn-vue`, `pinia`, `lucide-vue-next`, `reka-ui`, `@vueuse/core`, `vee-validate`, `vue-sonner`, `@vue/reactivity`, `zod` |
| `@tnzi/vant` | `@tnzi/core`, `vant`, `@vue/reactivity`, `zod` |
| `@tnzi/core` | `@vue/reactivity`, `zod` |

---

## @tnzi/core — 核心包

core 是整个生态的基础，不依赖任何 UI 框架。即使使用 UI 包，也需要了解 core 的 API。

### HTTP 客户端

所有与后端的通信都通过 `HttpClient` 完成。

#### 创建客户端

```typescript
import { createHttpClient } from '@tnzi/core'

const httpClient = createHttpClient({
  baseUrl: 'http://localhost:5000/api',
  timeout: 30000,

  // Token 过期时自动刷新
  refreshTokenFn: async () => {
    const result = await fetch('/api/auth/refresh-token', { ... })
    return result.accessToken
  },

  // Token 刷新失败时跳转登录
  onUnauthorized: () => {
    router.push('/login')
  },

  // 请求拦截器（如添加语言头）
  requestInterceptor: (config) => {
    config.headers = { ...config.headers, 'Accept-Language': 'zh-CN' }
    return config
  },
})
```

#### 基本请求

所有方法返回 `Promise<ApiResult<T>>`：

```typescript
// GET
const result = await httpClient.get<UserDto>('/users/profile')

// POST
const result = await httpClient.post<LoginResultDto>('/auth/login', {
  userName: 'admin',
  password: '123456',
})

// PUT / PATCH / DELETE
await httpClient.put<UserDto>('/users/123', updateData)
await httpClient.patch<UserDto>('/users/123', partialData)
await httpClient.delete('/users/123')
```

#### 文件操作

```typescript
// 上传（带进度）
const result = await httpClient.upload<FileUploadResultDto>('/files', file, {
  onProgress: (progress, loaded, total) => {
    console.log(`${progress}% (${loaded}/${total})`)
  },
})

// 下载
const blob = await httpClient.download('/files/123')

// Token 管理
httpClient.setAccessToken('eyJhbG...')
const token = httpClient.getAccessToken()
```

#### 响应处理工具

```typescript
import { isSuccess, isFailed, getErrorMessage, extractDataOrThrow } from '@tnzi/core'

const result = await httpClient.get<UserDto>('/users/profile')

if (isSuccess(result)) {
  console.log(result.data)           // UserDto
} else {
  console.error(getErrorMessage(result))
}

// 或直接解包（失败时抛 HttpError）
const user = extractDataOrThrow(result)
```

---

### 业务 API 契约

core 为每个后端模块提供了类型安全的 API 工厂函数。

#### Identity（身份认证）

```typescript
import { useAuthApi, useProfileApi, useAdminUserApi, useAdminRoleApi } from '@tnzi/core'

const auth = useAuthApi(httpClient)
const profile = useProfileApi(httpClient)
const adminUsers = useAdminUserApi(httpClient)
const adminRoles = useAdminRoleApi(httpClient)

// 登录
const result = await auth.loginWithRefreshToken({
  userName: 'admin',
  password: '123456',
})

// 获取当前用户
const me = await profile.get()

// 修改密码
await profile.changePassword({
  currentPassword: 'old',
  newPassword: 'new',
})

// 管理员：用户列表（分页）
const users = await adminUsers.getList({ pageIndex: 1, pageSize: 20 })

// 管理员：创建角色
await adminRoles.create({ name: 'Editor', permissions: ['articles.edit'] })
```

#### Storage（文件存储）

```typescript
import { useStorageApi, useAdminFileApi } from '@tnzi/core'

const storage = useStorageApi(httpClient)

// 上传文件
const result = await storage.upload(file, (progress) => {
  console.log(`上传进度: ${progress}%`)
})

// 下载文件
const blob = await storage.download('file-id')

// 获取预览/缩略图 URL
const previewUrl = storage.getPreviewUrl('file-id')
const thumbUrl = storage.getThumbnailUrl('file-id')

// 分片上传（大文件）
const session = await storage.initiateChunkedUpload({ fileName: 'large.zip', totalSize: 1024000 })
await storage.uploadChunk(session.data.id, 0, chunk0)
await storage.uploadChunk(session.data.id, 1, chunk1)
await storage.completeChunkedUpload(session.data.id)
```

#### 其他模块

```typescript
// 所有模块通过命名空间导出
import { Identity, Storage, Chat, Notification, Audit, Template, Payment, AI, System } from '@tnzi/core'
```

---

### 响应式状态管理器

core 提供 3 个基于 `@vue/reactivity` 的状态管理器，无需 Vue 组件即可使用。

#### StateDeps（公共依赖）

所有状态管理器共享同一组依赖：

```typescript
import { createHttpClient, createLocalStorageAdapter } from '@tnzi/core'

const deps = {
  httpClient: createHttpClient({ baseUrl: '/api' }),
  storage: createLocalStorageAdapter(),
  // theme: createNoopThemeAdapter(),   // 可选
  // router: createNoopRouterAdapter(), // 可选
}
```

#### AuthStateManager（认证状态）

```typescript
import { AuthStateManager } from '@tnzi/core'

const auth = new AuthStateManager(deps)

// ---- 操作 ----
await auth.login({ userName: 'admin', password: '123456' })
await auth.logout()
await auth.refreshAccessToken()
await auth.fetchUserProfile()
await auth.restoreAuth()                  // 从 localStorage 恢复登录态

// ---- 状态（响应式） ----
auth.isLoggedIn                           // boolean
auth.userName                             // string
auth.displayName                          // string（优先显示昵称）
auth.avatar                               // string | null
auth.accessToken                          // string | null
auth.isTokenExpired                       // boolean
auth.tokenExpiresIn                       // number（秒）

// ---- 权限检查 ----
auth.hasRole('admin')                     // boolean
auth.hasPermission('users.manage')        // boolean
auth.hasAnyRole(['admin', 'editor'])      // boolean
auth.hasAnyPermission(['a.view', 'a.edit']) // boolean
```

#### UserStateManager（用户偏好）

```typescript
import { UserStateManager } from '@tnzi/core'

const user = new UserStateManager(deps)

// ---- 操作 ----
await user.fetchCurrentUser()
user.updatePreferences({ theme: 'dark', language: 'en-US' })

// ---- 最近访问 & 收藏 ----
user.addRecentItem({ id: '1', name: 'Report Q4', type: 'document' })
user.addFavorite({ id: '2', name: 'Dashboard', type: 'page' })
user.isFavorite('2')                      // true
user.clearRecentItems()

// ---- 状态（响应式） ----
user.displayName                          // string
user.theme                                // 'light' | 'dark' | 'system'
user.language                             // 'zh-CN' | 'en-US' | ...
user.recentItemsCount                     // number
user.favoritesCount                       // number

// ---- 持久化 ----
user.loadPersistedData()                  // 从 storage 读取
user.persistData()                        // 保存到 storage
```

#### AppStateManager（应用全局状态）

```typescript
import { AppStateManager } from '@tnzi/core'

const app = new AppStateManager(deps)

// ---- 主题 & 语言 ----
app.setTheme('dark')
app.toggleTheme()                         // light → dark → system → light
app.setLanguage('en-US')

// ---- 侧边栏 ----
app.toggleSidebar()
app.setSidebarCollapsed(true)
app.setSidebarMode('fixed')               // 'responsive' | 'fixed' | 'drawer'

// ---- 全局 Loading ----
app.showLoading('Saving...')
app.hideLoading()

// ---- 通知 ----
app.showSuccess('Saved successfully')
app.showError('Network error')
app.showWarning('Disk almost full')
app.showInfo('New version available')
app.markAllNotificationsRead()
app.clearNotifications()

// ---- 模态框 ----
const id = app.openModal({ title: 'Confirm', content: 'Delete this item?' })
app.closeModal(id)
app.closeAllModals()

// ---- 状态（响应式） ----
app.isDarkMode                            // boolean
app.sidebarCollapsed                      // boolean
app.isLoading                             // boolean
app.unreadNotificationsCount              // number
app.hasOpenModal                          // boolean
app.isConnected                           // boolean
```

---

### 无头交互控制器

纯逻辑控制器，不绑定任何 UI 组件，可与任意 UI 库配合。

#### DataQueryController（数据查询，组合控制器）

最常用的控制器，内含分页、排序、选择：

```typescript
import { DataQueryController } from '@tnzi/core'

const query = new DataQueryController({
  fetchFn: (params) => httpClient.get('/users', { params }),
  pagination: { initialPage: 1, initialPageSize: 20 },
  sort: { defaultField: 'createdAt', defaultDirection: 'desc' },
  selection: { mode: 'multiple' },
  defaultFilter: { keyword: '', status: 'active' },
  immediate: true,                        // 创建后立即查询
})

// ---- 数据 ----
query.items                               // T[]
query.isLoading                           // boolean
query.isEmpty                             // boolean
query.hasData                             // boolean
query.error                               // string | null

// ---- 分页 ----
await query.changePage(2)
await query.changePageSize(50)
query.pagination.totalCount               // number
query.pagination.totalPages               // number
query.pagination.hasNextPage              // boolean

// ---- 排序 ----
await query.changeSort('name')            // 点击排序：asc → desc → clear

// ---- 筛选 ----
await query.applyFilter({ keyword: 'test' })
await query.resetFilter()

// ---- 选择 ----
query.selection.toggle('user-1')
query.selection.toggleAll()
query.selection.selectedKeys              // string[]
query.selection.selectedCount             // number

// ---- 刷新 & 重置 ----
await query.refresh()                     // 保持当前条件刷新
await query.reset()                       // 回到初始状态
```

#### PaginationController（独立分页）

```typescript
import { PaginationController } from '@tnzi/core'

const pager = new PaginationController({
  initialPage: 1,
  initialPageSize: 10,
  pageSizes: [10, 20, 50, 100],
})

pager.goTo(3)
pager.next()
pager.prev()
pager.setPageSize(50)
pager.updateFromResponse(totalCount)      // 接收后端返回的总数

// 生成查询参数
const params = pager.toQuery()            // { pageIndex, pageSize, skip, take }
```

#### FormController（表单控制器）

```typescript
import { FormController } from '@tnzi/core'
import { z } from 'zod'

const form = new FormController({
  initialValues: { name: '', email: '', age: 0 },
  schema: z.object({
    name: z.string().min(2),
    email: z.string().email(),
    age: z.number().min(0).max(150),
  }),
  onSubmit: async (values) => {
    await httpClient.post('/users', values)
  },
})

// ---- 操作 ----
form.setFieldValue('name', 'John')
form.touchField('email')
form.validate()                           // boolean
form.validateField('email')               // boolean
await form.submit()                       // 验证通过后调用 onSubmit

// ---- 状态 ----
form.values                               // { name, email, age }
form.isDirty                              // boolean
form.isValid                              // boolean
form.canSubmit                            // boolean（isValid && !isSubmitting）
form.isSubmitting                         // boolean
form.getFieldError('email')               // string | null
form.isFieldTouched('name')              // boolean

// ---- 重置 ----
form.reset()
```

#### SelectionController / SortController

```typescript
import { SelectionController, SortController } from '@tnzi/core'

// 选择
const selection = new SelectionController({ mode: 'multiple' })
selection.select('id-1')
selection.toggle('id-2')
selection.toggleAll()
selection.isSelected('id-1')              // true
selection.selectedKeys                    // ['id-1', 'id-2']

// 排序
const sort = new SortController({ defaultField: 'name', defaultDirection: 'asc' })
sort.toggle('name')                       // asc → desc → clear
sort.toQuery()                            // { sortBy: 'name', sortDescending: true }
```

---

### 适配器

适配器是 core 与 UI 框架的桥梁。core 定义接口，UI 包提供实现。

#### 内置适配器（可直接使用）

```typescript
import {
  createLocalStorageAdapter,
  createSessionStorageAdapter,
  createMemoryStorageAdapter,
  createNoopThemeAdapter,
  createNoopRouterAdapter,
} from '@tnzi/core'
```

#### 适配器接口（由 UI 包实现）

| 适配器 | 接口 | 说明 |
|---|---|---|
| `StorageAdapter` | `get/set/remove/clear` | 持久化存储 |
| `ThemeAdapter` | `applyTheme/getResolvedTheme` | 主题切换 |
| `RouterAdapter` | `push/replace/back/getCurrentPath` | 路由导航 |
| `MessageAdapter` | `success/error/warning/info/loading` | Toast 提示 |
| `DialogAdapter` | `confirm/alert/prompt` | 对话框 |

---

### 工具函数

```typescript
import {
  // 日期
  formatDate,                             // formatDate(date, 'yyyy-MM-dd HH:mm:ss')
  formatRelativeTime,                     // '5m ago', '2h ago', '3d ago'

  // 字符串
  truncate,                               // truncate('long text...', 20)
  capitalize,                             // 'hello' → 'Hello'
  slugify,                                // 'Hello World' → 'hello-world'
  randomString,                           // randomString(16)

  // 数字
  formatNumber,                           // 1234567 → '1,234,567'
  formatCurrency,                         // formatCurrency(99.9, 'USD') → '$99.90'
  formatFileSize,                         // 1048576 → '1 MB'
  clamp,                                  // clamp(150, 0, 100) → 100

  // 对象 & 数组
  deepClone,
  omit,
  pick,
  isEmpty,
  groupBy,
  unique,
  uniqueBy,
  chunk,

  // URL
  buildUrl,                               // buildUrl('/api/users', { page: 1 })
  parseQuery,

  // 函数
  debounce,
  throttle,

  // ID
  generateId,
  generateUuid,
} from '@tnzi/core'
```

---

### 类型与枚举

```typescript
import type {
  ApiResult,
  PagedList,
  PagedQueryDto,
  Nullable,
  DeepPartial,
  SelectOption,
  TreeNode,
  KeyValue,
} from '@tnzi/core'

import {
  Gender,
  EnableStatus,
  CommonStatus,
  OperationType,
  HttpStatus,
  HttpMethod,
  ContentType,
  DateRangePreset,
  // 工具函数
  getGenderLabel,
  isEnabled,
  isSuccessStatus,
  getStatusMessage,
} from '@tnzi/core'
```

---

## @tnzi/naive-ui — 管理后台

基于 Naive UI 的企业管理后台组件包。

### 初始化

```typescript
// main.ts
import { createApp } from 'vue'
import { createTnziNaiveUi } from '@tnzi/naive-ui'
import App from './App.vue'

const app = createApp(App)

app.use(createTnziNaiveUi({
  locale: 'zh-CN',                        // Naive UI 语言
  registerComponents: true,               // 全局注册 T* 组件（默认 true）
  registerAdapters: true,                 // 注册适配器（默认 true）
}))

app.mount('#app')
```

### 组件自动导入（可选）

配合 `unplugin-vue-components` 实现按需导入，无需手动 import：

```typescript
// vite.config.ts
import Components from 'unplugin-vue-components/vite'
import { TnziNaiveUiResolver } from '@tnzi/naive-ui'

export default defineConfig({
  plugins: [
    vue(),
    Components({
      resolvers: [TnziNaiveUiResolver()]
    }),
  ],
})
```

配置后，模板中直接使用 `<TDataTable />`，编译器自动处理导入。

### 组件一览

| 组件 | 说明 | 关键 Props |
|---|---|---|
| `TLoginForm` | 登录表单 | `showCaptcha`, `showSocialLogin`, `loading` |
| `TRegisterForm` | 注册表单 | `showUsername`, `showPhone`, `showCaptcha` |
| `TPasswordReset` | 密码重置 | — |
| `TDataTable` | 数据表格 | `data`, `columns`, `pagination`, `selectable`, `actions` |
| `TDataList` | 数据列表 | `data`, `itemKey`, `loading` |
| `TForm` | 通用表单 | `model`, `rules`, `labelPlacement` |
| `TDynamicForm` | 动态表单 | `model`, `fields`, `inline` |
| `TSearchForm` | 搜索表单 | `modelValue`, `placeholder`, `showReset` |
| `TStatCard` | 统计卡片 | `title`, `value`, `trend`, `color` |
| `TUserCard` | 用户卡片 | `user`, `showActions`, `actions` |
| `TAdminLayout` | 管理后台布局 | `sidebarItems`, `sidebarCollapsed`, `logo` |
| `TMenu` | 导航菜单 | `items`, `activeKey`, `openedKeys` |
| `TNavBar` | 顶部导航 | — |
| `TTabBar` | 标签导航 | — |

### 登录表单

```vue
<template>
  <TLoginForm
    :loading="loading"
    :show-captcha="true"
    :captcha-url="captchaUrl"
    :show-social-login="true"
    :social-providers="['Google', 'GitHub']"
    @submit="handleLogin"
    @forgot-password="router.push('/forgot')"
    @social-login="handleSocial"
    @refresh-captcha="refreshCaptcha"
  />
</template>

<script setup lang="ts">
import { ref } from 'vue'

const loading = ref(false)
const captchaUrl = ref('/api/auth/captcha?t=' + Date.now())

const handleLogin = async (credentials: {
  userName: string
  password: string
  rememberMe?: boolean
  captchaId?: string
  captchaCode?: string
}) => {
  loading.value = true
  try {
    const result = await auth.loginWithRefreshToken(credentials)
    // 处理登录结果...
  } finally {
    loading.value = false
  }
}

const refreshCaptcha = () => {
  captchaUrl.value = '/api/auth/captcha?t=' + Date.now()
}
</script>
```

### 数据表格

```vue
<template>
  <TSearchForm v-model="keyword" @search="handleSearch" @reset="handleReset" />

  <TDataTable
    :data="query.items"
    :columns="columns"
    :loading="query.isLoading"
    :selectable="true"
    :pagination="{
      pageIndex: query.pagination.pageIndex,
      pageSize: query.pagination.pageSize,
      total: query.pagination.totalCount,
    }"
    :actions="{ buttons: [
      { key: 'edit', label: 'Edit', type: 'primary' },
      { key: 'delete', label: 'Delete', type: 'danger' },
    ]}"
    @page-change="handlePageChange"
    @sort="handleSort"
    @action="handleAction"
    @update:selected-keys="handleSelect"
  />
</template>

<script setup lang="ts">
import { DataQueryController } from '@tnzi/core'

const columns = [
  { key: 'name', title: 'Name', sortable: true },
  { key: 'email', title: 'Email' },
  { key: 'status', title: 'Status', render: (row) => row.status === 1 ? 'Active' : 'Inactive' },
]

const query = new DataQueryController({
  fetchFn: (params) => httpClient.get('/users', { params }),
  pagination: { initialPageSize: 20 },
  sort: { defaultField: 'name' },
  defaultFilter: { keyword: '' },
  immediate: true,
})

const handlePageChange = (page: number, size: number) => query.changePage(page)
const handleSort = (field: string, order: 'asc' | 'desc') => query.changeSort(field)
const handleSearch = (keyword: string) => query.applyFilter({ keyword })
const handleReset = () => query.resetFilter()
const handleAction = (key: string, row: any) => {
  if (key === 'edit') router.push(`/users/${row.id}/edit`)
  if (key === 'delete') confirmDelete(row)
}
</script>
```

### 管理后台布局

```vue
<template>
  <TAdminLayout
    :sidebar-items="menuItems"
    :sidebar-collapsed="collapsed"
    logo="/logo.png"
    logo-text="My Admin"
    :show-footer="true"
    @sidebar-collapse="collapsed = $event"
    @menu-select="handleMenu"
  >
    <template #header>
      <div class="header-actions">
        <span>{{ auth.displayName }}</span>
        <button @click="auth.logout()">Logout</button>
      </div>
    </template>

    <!-- 主内容区 -->
    <RouterView />

    <template #footer>
      <p>© 2026 My Company</p>
    </template>
  </TAdminLayout>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const collapsed = ref(false)
const menuItems = [
  { key: 'dashboard', label: 'Dashboard' },
  {
    key: 'users',
    label: 'User Management',
    children: [
      { key: 'user-list', label: 'User List' },
      { key: 'role-list', label: 'Roles' },
    ],
  },
  { key: 'settings', label: 'Settings' },
]

const handleMenu = (key: string) => {
  const routes: Record<string, string> = {
    dashboard: '/dashboard',
    'user-list': '/users',
    'role-list': '/roles',
    settings: '/settings',
  }
  if (routes[key]) router.push(routes[key])
}
</script>
```

### 动态表单

```vue
<template>
  <TDynamicForm
    :model="formData"
    :fields="fields"
    :disabled="submitting"
    @submit="handleSubmit"
    @field-change="handleFieldChange"
  />
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'

const formData = reactive({ name: '', email: '', role: '', bio: '' })
const submitting = ref(false)

const fields = [
  { key: 'name', label: 'Name', type: 'text', required: true, placeholder: 'Enter name' },
  { key: 'email', label: 'Email', type: 'email', required: true },
  { key: 'role', label: 'Role', type: 'select', options: [
    { label: 'Admin', value: 'admin' },
    { label: 'Editor', value: 'editor' },
    { label: 'Viewer', value: 'viewer' },
  ]},
  { key: 'bio', label: 'Bio', type: 'textarea' },
]
</script>
```

### 统计卡片

```vue
<template>
  <div style="display: flex; gap: 16px;">
    <TStatCard title="Total Users" :value="12580" suffix="" :trend="12.5" color="blue" />
    <TStatCard title="Revenue" :value="89200" prefix="$" :trend="-3.2" color="green" />
    <TStatCard title="Orders" :value="1280" :trend="8.7" color="orange" />
    <TStatCard title="Errors" :value="23" :trend="0" color="red" />
  </div>
</template>
```

### 适配器

```typescript
import { useMessage, useDialog } from 'naive-ui'
import {
  createNaiveMessageAdapter,
  createNaiveDialogAdapter,
  createNaiveThemeAdapter,
} from '@tnzi/naive-ui'
import { setMessageAdapter, setDialogAdapter, setThemeAdapter } from '@tnzi/core'

// 在 setup 中注册（需要在 NMessageProvider / NDialogProvider 内部）
export default defineComponent({
  setup() {
    const message = useMessage()
    const dialog = useDialog()

    setMessageAdapter(createNaiveMessageAdapter(message))
    setDialogAdapter(createNaiveDialogAdapter(dialog))
    setThemeAdapter(createNaiveThemeAdapter())
  },
})
```

注册后，core 的状态管理器可以直接使用这些适配器：

```typescript
// 在任何地方
import { getMessageAdapter } from '@tnzi/core'

getMessageAdapter().success('Operation successful')
getMessageAdapter().error('Something went wrong')
```

---

## @tnzi/shadcn — Web 应用

基于 shadcn-vue + Tailwind CSS 的现代 Web 应用组件包。功能最完整，包含 Pinia Store。

### 初始化

```typescript
// main.ts
import { createApp } from 'vue'
import { createTnziUi } from '@tnzi/shadcn'
import App from './App.vue'

const app = createApp(App)

app.use(createTnziUi({
  locale: 'zh-CN',
  enablePinia: true,                      // 启用 Pinia 状态管理（默认 true）
  registerComponents: true,               // 全局注册 T* 组件（默认 true）
}))

app.mount('#app')
```

### 组件自动导入（可选）

```typescript
// vite.config.ts
import Components from 'unplugin-vue-components/vite'
import { TnziUiResolver } from '@tnzi/shadcn'

export default defineConfig({
  plugins: [
    vue(),
    Components({
      resolvers: [TnziUiResolver()]
    }),
  ],
})
```

### 组件一览

shadcn 包提供与 naive-ui 相同的 18 个 T* 语义组件，外加 60+ 个 shadcn-vue 原始组件：

**T* 语义组件**（同 naive-ui 的 API 契约）：

| 组件 | 说明 |
|---|---|
| `TLoginForm` | 登录表单 |
| `TRegisterForm` | 注册表单 |
| `TPasswordReset` | 密码重置 |
| `TDataTable` | 数据表格 |
| `TDataList` | 数据列表 |
| `TForm` | 通用表单 |
| `TDynamicForm` | 动态表单 |
| `TSearchForm` | 搜索表单 |
| `TStatCard` | 统计卡片 |
| `TUserCard` | 用户卡片 |
| `TAdminLayout` | 管理布局 |
| `TSidebar` | 侧边栏 |
| `THeader` | 顶部栏 |
| `TBreadcrumb` | 面包屑 |
| `TMenu` | 导航菜单 |
| `TNavBar` | 顶部导航 |
| `TTabBar` | 标签导航 |
| `TDialogProvider` | 对话框上下文 |

> 由于所有 T* 组件实现相同的 `@tnzi/core` 接口，模板代码可在 naive-ui 和 shadcn 之间通用。

### Pinia Store

shadcn 包内置了 3 个 Pinia Store，封装 core 的状态管理器：

#### useAuth()（认证 Store）

```typescript
import { useAuth } from '@tnzi/shadcn'

const auth = useAuth()

// 登录
await auth.login({ userName: 'admin', password: '123456' })

// 状态（ComputedRef，模板中自动解包）
auth.isLoggedIn                           // boolean
auth.userName                             // string
auth.displayName                          // string
auth.avatar                               // string | null
auth.isTokenExpired                       // boolean

// 权限
auth.hasRole('admin')
auth.hasPermission('users.manage')

// 登出
await auth.logout()
```

#### useUser()（用户偏好 Store）

```typescript
import { useUser } from '@tnzi/shadcn'

const user = useUser()

// 获取当前用户
await user.fetchCurrentUser()

// 偏好设置
user.setTheme('dark')
user.setLanguage('en-US')
await user.updatePreferences({ pageSize: 50, compactMode: true })

// 最近访问
user.addRecentItem({ id: '1', name: 'Report', type: 'document' })

// 收藏
user.addFavorite({ id: '2', name: 'Dashboard', type: 'page' })
user.isFavorite('2')                      // true

// 状态
user.displayName                          // string
user.theme                                // 'light' | 'dark' | 'system'
user.language                             // string
user.recentItemsCount                     // number
```

#### useApp()（应用全局状态 Store）

```typescript
import { useApp } from '@tnzi/shadcn'

const app = useApp()

// 主题
app.setTheme('dark')
app.toggleTheme()
app.isDarkMode                            // boolean

// 侧边栏
app.toggleSidebar()
app.sidebarCollapsed                      // boolean

// Loading
app.showLoading('Please wait...')
app.hideLoading()

// Toast 通知
app.showSuccess('Saved!')
app.showError('Network error')

// 模态框
const id = app.openModal({ title: 'Delete', content: 'Are you sure?' })
app.closeModal(id)
```

### Composables

```typescript
import { useShadcnTheme, useShadcnMessage, useShadcnDialog } from '@tnzi/shadcn'

// 主题
const { isDark, toggleTheme, setTheme } = useShadcnTheme()

// Toast（基于 vue-sonner）
const message = useShadcnMessage()
message.success('Done!')
message.error('Failed')

// 对话框
const dialog = useShadcnDialog()
const confirmed = await dialog.confirm({ title: 'Delete', content: 'Are you sure?' })
const input = await dialog.prompt('Enter your name')
```

### 对话框上下文

使用 `useShadcnDialog()` 需要在应用根部包裹 `TDialogProvider`：

```vue
<!-- App.vue -->
<template>
  <TDialogProvider>
    <RouterView />
  </TDialogProvider>
</template>
```

### shadcn-vue 原始组件

除 T* 组件外，shadcn 包还导出所有 shadcn-vue 原始组件，可直接使用：

```vue
<template>
  <Card>
    <CardHeader>
      <CardTitle>My Card</CardTitle>
    </CardHeader>
    <CardContent>
      <Button variant="outline" @click="handleClick">Click me</Button>
      <Badge variant="secondary">New</Badge>
    </CardContent>
  </Card>

  <Dialog>
    <DialogTrigger as-child>
      <Button>Open Dialog</Button>
    </DialogTrigger>
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Dialog Title</DialogTitle>
      </DialogHeader>
      <p>Dialog content here</p>
    </DialogContent>
  </Dialog>
</template>
```

---

## @tnzi/vant — 移动端

基于 Vant 4 的移动端组件包，适用于 H5、APP、PWA。

### 初始化

```typescript
// main.ts
import { createApp } from 'vue'
import { createTnziVant } from '@tnzi/vant'
import '@tnzi/vant/style.css'          // 必须引入样式
import App from './App.vue'

const app = createApp(App)

app.use(createTnziVant({
  locale: 'zh-CN',                        // Vant 语言
  registerVant: true,                     // 全局注册 T* 组件（默认 true）
}))

app.mount('#app')
```

### 组件一览

| 组件 | 说明 | 特点 |
|---|---|---|
| `TLoginForm` | 登录表单 | 支持验证码、社会化登录 |
| `TRegisterForm` | 注册表单 | 支持手机号、邮箱 |
| `TForm` | 通用表单 | 自定义验证规则 |
| `TDynamicForm` | 动态表单 | 从配置生成表单 |
| `TSearchForm` | 搜索表单 | 关键词搜索 |
| `TDataTable` | 数据表格 | 移动端卡片式布局 |
| `TDataList` | 数据列表 | **下拉刷新 + 无限滚动** |
| `TStatCard` | 统计卡片 | 趋势指示器 |
| `TUserCard` | 用户卡片 | 头像 + 状态徽章 |
| `TMenu` | 导航菜单 | 可折叠层级菜单 |
| `TNavBar` | 顶部导航栏 | 返回按钮、安全区域适配 |
| `TTabBar` | 底部标签栏 | 固定底部、徽章 |

### 移动端数据列表（下拉刷新 + 无限滚动）

vant 包的 `TDataList` 针对移动端优化，支持手势下拉刷新和滚动加载：

```vue
<template>
  <TDataList
    :items="items"
    :load-state="{ loading, noMore }"
    :pull-to-refresh="true"
    item-key="id"
    empty-text="No data"
    @refresh="handleRefresh"
    @load-more="handleLoadMore"
    @item-click="handleItemClick"
  >
    <template #item="{ item }">
      <div class="item-card">
        <h3>{{ item.name }}</h3>
        <p>{{ item.description }}</p>
      </div>
    </template>
  </TDataList>
</template>

<script setup lang="ts">
const items = ref([])
const loading = ref(false)
const noMore = ref(false)
let page = 1

const handleRefresh = async () => {
  page = 1
  noMore.value = false
  const result = await httpClient.get('/items', { params: { page: 1 } })
  items.value = result.data.items
}

const handleLoadMore = async () => {
  loading.value = true
  page++
  const result = await httpClient.get('/items', { params: { page } })
  items.value.push(...result.data.items)
  noMore.value = !result.data.hasNextPage
  loading.value = false
}
</script>
```

### 底部标签栏 + 顶部导航栏

```vue
<template>
  <!-- 顶部导航 -->
  <TNavBar
    title="My App"
    :show-back="true"
    :fixed="true"
    :safe-area-inset-top="true"
    @back="router.back()"
  >
    <template #right>
      <van-icon name="search" @click="openSearch" />
    </template>
  </TNavBar>

  <!-- 主内容 -->
  <div style="padding-top: 46px; padding-bottom: 50px;">
    <RouterView />
  </div>

  <!-- 底部标签栏 -->
  <TTabBar
    v-model:active-key="activeTab"
    :tabs="tabs"
    :fixed="true"
    :safe-area-inset-bottom="true"
    :badge="{ messages: unreadCount }"
    @change="handleTabChange"
  />
</template>

<script setup lang="ts">
const activeTab = ref('home')
const unreadCount = ref(3)

const tabs = [
  { key: 'home', label: 'Home', icon: 'home-o' },
  { key: 'messages', label: 'Messages', icon: 'chat-o' },
  { key: 'profile', label: 'Profile', icon: 'user-o' },
]

const handleTabChange = (key: string) => {
  router.push(`/${key}`)
}
</script>
```

### 移动端视口检测

```typescript
import { useMobileViewport } from '@tnzi/vant'

const { width, isMobile } = useMobileViewport(768)

// 在模板中根据设备类型调整布局
// <div :class="isMobile ? 'mobile-layout' : 'desktop-layout'">
```

### 适配器

```typescript
import { createVantMessageAdapter, createVantDialogAdapter } from '@tnzi/vant'
import { setMessageAdapter, setDialogAdapter } from '@tnzi/core'

// 注册适配器
setMessageAdapter(createVantMessageAdapter())
setDialogAdapter(createVantDialogAdapter())

// 使用
import { getMessageAdapter, getDialogAdapter } from '@tnzi/core'

getMessageAdapter().success('Saved!')           // Vant showSuccessToast
getMessageAdapter().loading('Loading...')       // Vant showLoadingToast

const ok = await getDialogAdapter().confirm('Delete this item?')  // Vant showConfirmDialog
```

### 注意事项

- vant 包**不包含** Pinia Store，需自行管理状态或配合 `@tnzi/core` 的状态管理器
- 移动端 `TDataTable` 渲染为**卡片式**布局（不是传统表格），更适合小屏
- 记得引入 `@tnzi/vant/style.css` 以加载 Vant 基础样式

---

## 完整示例

### 示例 1：企业后台（naive-ui）

```
my-admin/
├── src/
│   ├── main.ts                # 应用入口
│   ├── App.vue                # 根组件（TAdminLayout）
│   ├── http.ts                # HttpClient 配置
│   ├── router/index.ts        # Vue Router
│   └── views/
│       ├── Login.vue          # TLoginForm
│       ├── Dashboard.vue      # TStatCard
│       └── users/
│           └── List.vue       # TDataTable + TSearchForm
├── package.json
├── vite.config.ts
└── tsconfig.json
```

**main.ts**

```typescript
import { createApp } from 'vue'
import { createTnziNaiveUi } from '@tnzi/naive-ui'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'

const app = createApp(App)
app.use(createPinia())
app.use(createTnziNaiveUi({ locale: 'zh-CN' }))
app.use(router)
app.mount('#app')
```

**http.ts**

```typescript
import { createHttpClient } from '@tnzi/core'
import router from './router'

export const httpClient = createHttpClient({
  baseUrl: import.meta.env.VITE_API_URL || 'http://localhost:5000/api',
  timeout: 30000,
  onUnauthorized: () => router.push('/login'),
})
```

**views/users/List.vue**

```vue
<template>
  <div>
    <h2>User Management</h2>

    <TSearchForm v-model="keyword" @search="handleSearch" @reset="handleReset" />

    <TDataTable
      :data="query.items"
      :columns="columns"
      :loading="query.isLoading"
      :pagination="{
        pageIndex: query.pagination.pageIndex,
        pageSize: query.pagination.pageSize,
        total: query.pagination.totalCount,
      }"
      :actions="{ buttons: [
        { key: 'edit', label: 'Edit', type: 'primary' },
        { key: 'delete', label: 'Delete', type: 'danger' },
      ]}"
      @page-change="(p) => query.changePage(p)"
      @action="handleAction"
    />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { DataQueryController, useAdminUserApi } from '@tnzi/core'
import { httpClient } from '@/http'

const keyword = ref('')
const userApi = useAdminUserApi(httpClient)

const columns = [
  { key: 'userName', title: 'Username', sortable: true },
  { key: 'email', title: 'Email' },
  { key: 'creationTime', title: 'Created' },
]

const query = new DataQueryController({
  fetchFn: (params) => userApi.getList(params),
  pagination: { initialPageSize: 20 },
  immediate: true,
})

const handleSearch = (kw: string) => query.applyFilter({ keyword: kw })
const handleReset = () => query.resetFilter()
const handleAction = (key: string, row: any) => {
  // ...
}
</script>
```

### 示例 2：移动端 APP（vant）

```
my-mobile/
├── src/
│   ├── main.ts
│   ├── App.vue                # TNavBar + TTabBar + RouterView
│   ├── http.ts
│   └── views/
│       ├── Home.vue           # TStatCard
│       ├── Messages.vue       # TDataList（无限滚动）
│       └── Profile.vue        # TUserCard
├── package.json
└── vite.config.ts
```

**App.vue**

```vue
<template>
  <TNavBar :title="pageTitle" :show-back="showBack" @back="router.back()" />

  <main style="padding: 46px 0 50px;">
    <RouterView />
  </main>

  <TTabBar
    v-model:active-key="activeTab"
    :tabs="tabs"
    :badge="{ messages: unread }"
    @change="(key) => router.push(`/${key}`)"
  />
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()
const activeTab = ref('home')
const unread = ref(5)

const showBack = computed(() => route.meta.showBack !== false)
const pageTitle = computed(() => String(route.meta.title || 'My App'))

const tabs = [
  { key: 'home', label: 'Home', icon: 'home-o' },
  { key: 'messages', label: 'Messages', icon: 'chat-o' },
  { key: 'profile', label: 'Profile', icon: 'contact-o' },
]
</script>
```

---

## 附录

### 跨包通用的组件 API

所有 T* 组件在三个 UI 包中实现**相同的接口**（定义在 `@tnzi/core/components`），这意味着：

1. **模板代码通用** — 只需换包名，不改模板
2. **Props / Events 一致** — 同样的 `columns`、`data`、`@submit` 等
3. **类型安全** — TypeScript 类型来自 core，三个包共享

```vue
<!-- 这段模板在 naive-ui、shadcn、vant 三个包中完全一致 -->
<TLoginForm
  :loading="loading"
  :show-captcha="showCaptcha"
  @submit="handleLogin"
  @forgot-password="handleForgot"
/>
```

### 迁移指南

如果需要从一个 UI 包迁移到另一个：

1. 替换 `package.json` 中的依赖（如 `@tnzi/naive-ui` → `@tnzi/shadcn`）
2. 修改 `main.ts` 中的插件引入（`createTnziNaiveUi` → `createTnziUi`）
3. T* 组件模板代码**无需修改**
4. 适配器注册代码需要替换为对应 UI 包的实现
5. 如果使用了原生 UI 组件（如 `<n-button>`），需要替换为新 UI 库的组件
