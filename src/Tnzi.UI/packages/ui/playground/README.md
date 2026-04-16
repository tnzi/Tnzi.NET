# @tnzi/ui Playground

Naive UI 组件库的完整展示和测试环境。

## 功能特性

### 1. 适配器示例
- **NaiveThemeAdapter**: 主题切换适配器 (亮色/暗色模式)
- **NaiveMessageAdapter**: 消息提示适配器 (info/success/warning/error)
- **NaiveDialogAdapter**: 对话框适配器 (info/success/warning/error/confirm)

### 2. Pinia Stores 示例
- **useAuthStore**: 认证状态管理 (token, isAuthenticated)
- **useUserStore**: 用户信息管理 (profile)
- **useAppStore**: 应用配置管理 (theme, locale)

### 3. Naive UI 原生组件示例
展示 Naive UI 的核心组件:
- 按钮 (Button)
- 卡片 (Card)
- 表单 (Form)
- 数据表格 (Data Table)
- 标签 (Tag)
- 徽标 (Badge)
- 进度条 (Progress)
- 时间轴 (Timeline)

### 4. T* 业务组件 (开发中)
待开发的 Tnzi 业务组件:
- TLoginForm - 登录表单
- TRegisterForm - 注册表单
- TDataTable - 数据表格
- TDataList - 数据列表
- TForm - 通用表单
- TDynamicForm - 动态表单
- TSearchForm - 搜索表单
- TStatCard - 统计卡片
- TUserCard - 用户卡片
- TMenu - 菜单
- TNavBar - 导航栏
- TTabBar - 标签栏

## 运行说明

```bash
# 从项目根目录安装依赖
pnpm install

# 启动开发服务器
cd packages/naive-ui/playground
pnpm dev

# 访问
http://localhost:3004
```

## 项目结构

```
playground/
├── src/
│   ├── App.vue          # 主应用组件 (大杂烩展示页)
│   └── main.ts          # 应用入口 (注册 Pinia + TnziNaiveUi 插件)
├── index.html           # HTML 模板
├── vite.config.ts       # Vite 配置
├── tsconfig.json        # TypeScript 配置
└── package.json         # 项目依赖
```

## 开发指南

### 添加新的适配器示例

在 `App.vue` 的 "适配器示例" 区域添加新的演示代码:

```vue
<div>
  <n-h3>NewAdapter</n-h3>
  <n-text depth="3">适配器描述</n-text>
  <n-divider />
  <!-- 适配器测试代码 -->
</div>
```

### 添加新的 T* 组件示例

1. 确保组件已在 `@tnzi/ui/components` 中导出
2. 在 "T* 业务组件" 区域添加组件演示:

```vue
<div>
  <n-h3>TComponentName</n-h3>
  <TComponentName :prop="value" @event="handler" />
</div>
```

### 添加新的 Store 示例

1. 从 `@tnzi/ui/stores` 导入 store
2. 在 "Pinia Stores 示例" 区域添加演示代码

## 技术栈

- **Vue 3.5+** - 渐进式 JavaScript 框架
- **Naive UI 2.40+** - Vue 3 组件库
- **Pinia 2.3+** - Vue 状态管理
- **Vite 5.4+** - 前端构建工具
- **TypeScript 5.9+** - JavaScript 的超集

## 注意事项

1. **端口号**: 默认使用 `3004` 端口 (vant: 3003, element-plus: 3005)
2. **依赖关系**: 自动链接到 `@tnzi/core` 和 `@tnzi/ui` workspace 包
3. **热更新**: 修改源代码会自动触发热更新
4. **主题切换**: 使用 `n-config-provider` 包裹,支持亮色/暗色主题切换
5. **语言切换**: 默认使用中文 (zhCN),可通过 appStore 切换语言

## 相关链接

- [Naive UI 官方文档](https://www.naiveui.com/)
- [Vue 3 文档](https://vuejs.org/)
- [Pinia 文档](https://pinia.vuejs.org/)
- [Vite 文档](https://vitejs.dev/)
