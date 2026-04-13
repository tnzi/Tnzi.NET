# npm 发布指南

> 本文档说明如何将 `@tnzi/*` 包发布到 npm registry。

## 目录

1. [前置准备](#前置准备)
2. [快速发布（推荐）](#快速发布推荐)
3. [Changeset 标准流程](#changeset-标准流程)
4. [手动发布](#手动发布)
5. [版本号管理](#版本号管理)
6. [常见场景](#常见场景)
7. [故障排查](#故障排查)

---

## 前置准备

### 1. npm 登录（仅首次）

```bash
npm login
```

登录后 token 保存在 `~/.npmrc`，之后不需要重复登录。

验证登录状态：

```bash
npm whoami
```

### 2. 确认构建正常

```bash
cd src/Tnzi.UI
pnpm build
```

5 个包必须全部构建成功，无 TS 错误。

---

## 快速发布（推荐）

项目提供了 `tools/release.mjs` 快捷工具，一行命令完成 **版本升级 → 构建 → 发布**。

### 命令格式

```bash
pnpm release <包名> [版本类型]
```

| 参数 | 可选值 | 说明 |
|------|--------|------|
| 包名 | `core`, `ui`, `ui-admin`, `ai`, `mobile`, `all` | 要发布的包 |
| 版本类型 | `patch`(默认), `minor`, `major`, `x.y.z` | 版本升级方式 |

### 常用命令

```bash
# 查看所有包的本地版本和 npm 版本
pnpm release:status

# patch 发布（最常用，修 bug / 小调整）
pnpm release ui                    # 0.1.0 → 0.1.1
pnpm release mobile                # 0.1.0 → 0.1.1

# minor 发布（新增功能）
pnpm release ui minor              # 0.1.0 → 0.2.0

# major 发布（破坏性变更）
pnpm release core major            # 0.1.0 → 1.0.0

# 指定具体版本号
pnpm release ui-admin 0.2.0

# 全部包同时发布（core 自动排首位）
pnpm release all patch

# 试运行（不实际发布，不修改版本号）
pnpm release --dry ui
```

### 执行流程

`pnpm release ui patch` 内部依次执行：

1. 检查 npm 登录状态
2. 读取 `packages/ui/package.json` 中的当前版本
3. 计算新版本号（patch: `0.1.0` → `0.1.1`）
4. 写入新版本号到 `package.json`
5. 执行 `pnpm --filter @tnzi/ui build`
6. 执行 `pnpm --filter @tnzi/ui publish --no-git-checks`

---

## Changeset 标准流程

适用于需要生成 changelog、多人协作、CI/CD 的正式发布流程。

### 1. 添加变更记录

```bash
pnpm version:add
```

交互式界面会让你选择：
- 哪些包有变更（可多选）
- 变更类型（patch / minor / major）
- 变更描述

完成后会在 `.changeset/` 下生成一个 markdown 文件记录变更。

### 2. 应用版本号

```bash
pnpm version:apply
```

Changeset 会根据变更记录：
- 自动修改受影响包的 `package.json` 版本号
- 更新包之间的依赖版本引用
- 生成 CHANGELOG.md

### 3. 发布

```bash
# 全量发布（构建 + 发布所有有新版本的包）
pnpm publish:release

# 试运行（不实际发布）
pnpm publish:dry
```

### 4. 检查状态

```bash
# 查看待发布的变更
pnpm version:check
```

---

## 手动发布

不借助任何工具，直接操作：

```bash
# 1. 手动修改 packages/ui/package.json 中的 version 字段

# 2. 构建
pnpm --filter @tnzi/ui build

# 3. 发布
pnpm --filter @tnzi/ui publish --no-git-checks

# 试运行
pnpm --filter @tnzi/ui publish --no-git-checks --dry-run
```

---

## 版本号管理

### 版本号存放位置

每个包的 `package.json` 中的 `version` 字段：

```
packages/core/package.json       → "version": "0.1.2"
packages/ui/package.json         → "version": "0.1.2"
packages/ui-admin/package.json   → "version": "0.1.0"
packages/ui-ai/package.json      → "version": "0.1.0"
packages/mobile/package.json     → "version": "0.1.0"
```

### 版本策略

各包**独立版本**，互不影响（`.changeset/config.json` 中 `fixed: []`）。

| 版本类型 | 规则 | 适用场景 |
|----------|------|----------|
| `patch` | `0.1.0` → `0.1.1` | Bug 修复、样式微调、文案修改 |
| `minor` | `0.1.0` → `0.2.0` | 新增组件、新增功能、向后兼容的变更 |
| `major` | `0.1.0` → `1.0.0` | 破坏性变更、API 重构 |

### 包间依赖

UI 包依赖 core 使用 `workspace:*`。发布时 pnpm 自动将其替换为实际版本号（如 `0.1.2`）。

- 只改了 UI 包 → 只需发布该 UI 包，core 不受影响
- 改了 core → 建议一起发布依赖它的 UI 包（`pnpm release all`）

---

## 常见场景

### 场景 1：修复 ui 某个组件的样式

```bash
# 1. 修改代码
# 2. 试运行确认没问题
pnpm release --dry ui

# 3. 正式发布 patch
pnpm release ui
```

### 场景 2：给 mobile 包新增一个组件

```bash
# 1. 开发组件，测试通过
# 2. minor 发布
pnpm release mobile minor
```

### 场景 3：core 接口变更，需要同步所有包

```bash
# 1. 修改 core 代码
# 2. 相应修改各 UI 包
# 3. 构建验证
pnpm build

# 4. 全量发布
pnpm release all minor
```

### 场景 4：首次发布

```bash
# 1. 登录 npm
npm login

# 2. 试运行确认包内容
pnpm release --dry all

# 3. 正式发布
pnpm release all
```

---

## 故障排查

### npm token 过期

```
npm error code ENEEDAUTH
npm notice Access token expired or revoked
```

解决：重新执行 `npm login`。

### 包名被占用

```
npm error code E403
npm error 403 Forbidden - PUT https://registry.npmjs.org/@tnzi%2fcore
```

解决：确认 npm 组织 `@tnzi` 已创建且你有发布权限。

### 版本号已存在

```
npm error code EPUBLISHCONFLICT
npm error 409 Conflict
```

解决：不能重复发布相同版本号，需要升版本后重新发布。

### 构建失败

```bash
# 清理后重新构建
pnpm clean
pnpm build
```

### 类型声明缺失

确认各 UI 包的 `vite.config.ts` 中 `vite-plugin-dts` 配置了 `entryRoot`：

```ts
dts({
  include: ['src/**/*'],
  outDir: 'dist',
  entryRoot: resolve(__dirname, 'src'),  // 必须
})
```

---

## 配置文件一览

| 文件 | 说明 |
|------|------|
| `.npmrc` | npm registry 配置（`registry`, `access`） |
| `.changeset/config.json` | Changeset 版本策略配置 |
| `packages/*/package.json` | 各包版本号、`publishConfig`、`files` |
| `tools/release.mjs` | 快捷发布脚本 |

## 相关命令速查

| 命令 | 说明 |
|------|------|
| `pnpm release:status` | 查看各包版本 |
| `pnpm release <包> [类型]` | 快速发布 |
| `pnpm release --dry <包>` | 试运行 |
| `pnpm release all` | 全量发布 |
| `pnpm version:add` | 添加 changeset 变更记录 |
| `pnpm version:apply` | 应用版本号 |
| `pnpm version:check` | 查看待发布变更 |
| `pnpm publish:release` | changeset 全量发布 |
| `pnpm publish:dry` | changeset 试运行 |
| `pnpm build` | 构建所有包 |
| `pnpm clean` | 清理所有 dist |
| `npm login` | 登录 npm |
| `npm whoami` | 验证登录状态 |
